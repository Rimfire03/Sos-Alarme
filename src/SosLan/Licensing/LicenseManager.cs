using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SosLan.Licensing;

public sealed record ActivationResult(bool Success, string? Message);

/// <summary>
/// Logique de licence commune à Windows et macOS : activation, validation, grâce hors-ligne (port de
/// client/licenseClient.js), suppression/changement. Les parties dépendantes de la plateforme
/// (stockage, identifiant machine) sont injectées.
/// </summary>
public sealed class LicenseManager
{
    private readonly LicenseApiClient _api = new();
    private readonly ILicenseStorage _storage;
    private readonly string _deviceId;
    private readonly string _deviceName = Environment.MachineName;

    private LicenseManager(ILicenseStorage storage, string deviceId, bool isFree)
    {
        _storage = storage;
        _deviceId = deviceId;
        IsFree = isFree;
    }

    /// <summary>Licence gratuite : interrupteur global désactivé ou licence.ini présent. Aucun contrôle ni appel réseau.</summary>
    public bool IsFree { get; }

    public LicenseInfo? CurrentLicense { get; private set; }

    /// <summary>Vrai si la validation courante repose sur la grâce hors-ligne.</summary>
    public bool IsOffline { get; private set; }

    public DateTimeOffset? GraceUntil { get; private set; }

    /// <summary>
    /// Crée le gestionnaire. Le test licence.ini est le tout premier du démarrage : en mode gratuit,
    /// ni le stockage ni l'identifiant machine ne sont lus.
    /// </summary>
    public static LicenseManager Create()
    {
        if (!LicenseConfig.Enabled || File.Exists(Path.Combine(LicensePlatform.GetInstallDirectory(), LicenseConfig.FreeLicenseFileName)))
        {
            return new LicenseManager(new NullStorage(), "", isFree: true);
        }

        var deviceId = ComputeDeviceId(LicensePlatform.CreateMachineIdProvider().GetRawMachineId());
        return new LicenseManager(LicensePlatform.CreateStorage(), deviceId, isFree: false);
    }

    /// <summary>Hash SHA-256 (hex minuscule) : l'identifiant brut n'est jamais envoyé.</summary>
    public static string ComputeDeviceId(string rawMachineId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawMachineId))).ToLowerInvariant();

    public bool HasStoredLicense => LoadStored() != null;

    // ----- Contrôle (port de checkLicense) -----

    /// <summary>Valide en ligne la licence stockée ; si le serveur est injoignable, applique la grâce hors-ligne.</summary>
    public async Task<LicenseCheckResult> CheckStoredAsync()
    {
        var stored = LoadStored();
        if (stored == null)
        {
            return new LicenseCheckResult(false, false, "no_license", null, null);
        }

        LicenseApiResponse response;
        try
        {
            response = await _api.ValidateAsync(stored.LicenseKey, _deviceId);
        }
        catch (LicenseServerUnreachableException)
        {
            return CheckOfflineGrace(stored);
        }

        if (response.Valid)
        {
            var updated = new StoredLicense
            {
                LicenseKey = stored.LicenseKey,
                ProductSlug = LicenseConfig.ProductSlug,
                DeviceId = _deviceId,
                LastValidatedAt = DateTimeOffset.UtcNow,
                License = response.License
            };
            TrySave(updated);
            SetCurrent(response.License, offline: false, graceUntil: null);
            return new LicenseCheckResult(true, false, null, response.License, null);
        }

        // Refus explicite (révoquée, expirée, poste non activé...) : réponse ferme, jamais de grâce.
        return new LicenseCheckResult(false, false, response.Reason, null, null);
    }

    private LicenseCheckResult CheckOfflineGrace(StoredLicense stored)
    {
        if (stored.ProductSlug != LicenseConfig.ProductSlug || stored.DeviceId != _deviceId)
        {
            return new LicenseCheckResult(false, true, "server_unreachable_no_prior_validation", null, null);
        }

        var deadline = stored.LastValidatedAt.AddDays(LicenseConfig.GraceDays);
        if (stored.License?.ExpiresAt is { } expiresAt && expiresAt < deadline)
        {
            deadline = expiresAt;
        }

        if (DateTimeOffset.UtcNow <= deadline)
        {
            SetCurrent(stored.License, offline: true, graceUntil: deadline);
            return new LicenseCheckResult(true, true, null, stored.License, deadline);
        }

        return new LicenseCheckResult(false, true, "offline_grace_expired", null, null);
    }

    // ----- Activation / changement / suppression -----

    /// <summary>Active une clé sur ce poste et la stocke. En cas d'échec, le stockage existant reste intact.</summary>
    public async Task<ActivationResult> ActivateAsync(string licenseKey)
    {
        licenseKey = licenseKey.Trim();
        if (licenseKey.Length == 0)
        {
            return new ActivationResult(false, "Saisissez votre clé de licence.");
        }

        LicenseApiResponse response;
        try
        {
            response = await _api.ActivateAsync(licenseKey, _deviceId, _deviceName);
        }
        catch (LicenseServerUnreachableException)
        {
            return new ActivationResult(false, LicenseMessages.ForReason("server_unreachable"));
        }

        if (!response.Valid)
        {
            return new ActivationResult(false, LicenseMessages.ForReason(response.Reason));
        }

        try
        {
            _storage.Save(Serialize(new StoredLicense
            {
                LicenseKey = licenseKey,
                ProductSlug = LicenseConfig.ProductSlug,
                DeviceId = _deviceId,
                LastValidatedAt = DateTimeOffset.UtcNow,
                License = response.License
            }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or CryptographicException)
        {
            return new ActivationResult(false, "La licence est valide mais n'a pas pu être enregistrée sur ce poste.");
        }

        SetCurrent(response.License, offline: false, graceUntil: null);
        return new ActivationResult(true, null);
    }

    /// <summary>
    /// Remplace la licence : la nouvelle clé est d'abord activée ; seulement en cas de succès l'ancienne est
    /// désactivée côté serveur. En cas d'échec, l'ancienne licence reste intacte.
    /// </summary>
    public async Task<ActivationResult> ChangeAsync(string newLicenseKey)
    {
        var previous = LoadStored();
        var result = await ActivateAsync(newLicenseKey);

        if (result.Success && previous != null && !string.Equals(previous.LicenseKey, newLicenseKey.Trim(), StringComparison.Ordinal))
        {
            try
            {
                await _api.DeactivateAsync(previous.LicenseKey, _deviceId);
            }
            catch (LicenseServerUnreachableException)
            {
                // Au mieux : la nouvelle licence est déjà active, l'ancienne activation sera libérée par l'administrateur.
            }
        }

        return result;
    }

    /// <summary>
    /// Libère le poste côté serveur puis efface le stockage. Renvoie false (sans rien effacer) si le serveur
    /// est injoignable et que <paramref name="deleteEvenIfUnreachable"/> est faux.
    /// </summary>
    public async Task<bool> RemoveAsync(bool deleteEvenIfUnreachable)
    {
        var stored = LoadStored();
        if (stored != null)
        {
            try
            {
                await _api.DeactivateAsync(stored.LicenseKey, _deviceId);
            }
            catch (LicenseServerUnreachableException)
            {
                if (!deleteEvenIfUnreachable)
                {
                    return false;
                }
            }
        }

        ClearStorage();
        return true;
    }

    /// <summary>Efface la licence stockée (sans appel réseau).</summary>
    public void ClearStorage()
    {
        _storage.Delete();
        CurrentLicense = null;
        IsOffline = false;
        GraceUntil = null;
    }

    // ----- Utilitaires -----

    private void SetCurrent(LicenseInfo? license, bool offline, DateTimeOffset? graceUntil)
    {
        CurrentLicense = license;
        IsOffline = offline;
        GraceUntil = graceUntil;
    }

    private StoredLicense? LoadStored()
    {
        var raw = _storage.Load();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredLicense>(raw, LicenseJson.Options);
            return stored == null || string.IsNullOrWhiteSpace(stored.LicenseKey) ? null : stored;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void TrySave(StoredLicense stored)
    {
        try
        {
            _storage.Save(Serialize(stored));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or CryptographicException)
        {
            // La validation reste acquise pour cette session ; la date de dernière validation ne sera pas rafraîchie.
        }
    }

    private static string Serialize(StoredLicense stored) => JsonSerializer.Serialize(stored, LicenseJson.Options);

    private sealed class NullStorage : ILicenseStorage
    {
        public string? Load() => null;
        public void Save(string value) { }
        public void Delete() { }
    }
}

/// <summary>Messages utilisateur (français), identiques sur les deux plateformes.</summary>
public static class LicenseMessages
{
    public static string ForReason(string? reason) => reason switch
    {
        "license_not_found" => "Clé de licence introuvable. Vérifiez votre saisie.",
        "license_revoked" => "Cette licence a été révoquée.",
        "license_expired" => "Cette licence a expiré.",
        "activation_limit_reached" => "Le nombre maximal de postes activés pour cette licence est atteint.",
        "device_not_activated" => "Ce poste n'est plus activé pour cette licence.",
        "server_unreachable" or "server_unreachable_no_prior_validation" =>
            "Impossible de joindre le serveur de licences. Vérifiez votre connexion réseau et réessayez.",
        "offline_grace_expired" =>
            $"Le serveur de licences est injoignable et la période d'utilisation hors-ligne ({LicenseConfig.GraceDays} jours) est dépassée. Connectez-vous au réseau pour revalider la licence.",
        _ => "La licence a été refusée par le serveur."
    };
}
