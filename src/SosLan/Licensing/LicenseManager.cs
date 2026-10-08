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
    private ILicenseStorage _storage;
    private string? _deviceIdValue;
    private readonly string _deviceName = Environment.MachineName;

    private LicenseManager(ILicenseStorage storage, string? deviceId, bool isFree)
    {
        _storage = storage;
        _deviceIdValue = deviceId;
        IsFree = isFree;
    }

    /// <summary>Identifiant du poste (haché). En mode gratuit il n'est calculé qu'à la demande (ping), jamais au démarrage.</summary>
    private string _deviceId => _deviceIdValue ??= ComputeDeviceId(LicensePlatform.CreateMachineIdProvider().GetRawMachineId());

    /// <summary>Licence gratuite : interrupteur global désactivé ou licence.ini présent. Aucun contrôle de licence.</summary>
    public bool IsFree { get; private set; }

    /// <summary>Nom du client lu dans licence.ini (ou reçu via bypassName) ; null si aucun. Seul contenu affiché.</summary>
    public string? FreeName { get; private set; }

    private static string FreeFilePath => Path.Combine(LicensePlatform.GetInstallDirectory(), LicenseConfig.FreeLicenseFileName);

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
        if (!LicenseConfig.Enabled || File.Exists(FreeFilePath))
        {
            return new LicenseManager(new NullStorage(), null, isFree: true);
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

            if (response.Commands?.Contains("install_bypass") == true)
            {
                await InstallBypassAsync(response.BypassName);
            }

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

        return Store(licenseKey, response.License) ?? new ActivationResult(true, null);
    }

    /// <summary>
    /// Demande une démo pour ce poste (aucune saisie de clé). Le serveur l'active directement : la clé reçue est
    /// stockée telle quelle, sans jamais être affichée, copiée ni journalisée.
    /// </summary>
    public async Task<ActivationResult> RequestDemoAsync()
    {
        LicenseApiResponse response;
        try
        {
            response = await _api.RequestDemoAsync(_deviceId, _deviceName);
        }
        catch (LicenseServerUnreachableException)
        {
            return new ActivationResult(false, LicenseMessages.ForReason("server_unreachable"));
        }

        if (!response.Valid)
        {
            // Messages propres à la démo (le serveur répond 403 license_expired / license_revoked pour ce poste).
            return new ActivationResult(false, response.Reason switch
            {
                "license_expired" => "La démo de ce poste est terminée. Saisissez une clé de licence.",
                "license_revoked" => "Cette démo a été désactivée. Saisissez une clé de licence.",
                _ => LicenseMessages.ForReason(response.Reason)
            });
        }

        // 201 (nouvelle démo) et 200 (démo déjà existante, même clé et même échéance) : traités à l'identique.
        var key = response.License?.Key;
        if (string.IsNullOrWhiteSpace(key))
        {
            return new ActivationResult(false, "La démo n'a pas pu être activée. Réessayez ou saisissez une clé de licence.");
        }

        var failure = Store(key, response.License);
        if (failure != null)
        {
            return failure;
        }

        var message = response.License?.ExpiresAt is { } expiresAt
            ? $"Démo activée, valable jusqu'au {expiresAt.LocalDateTime:dd/MM/yyyy}."
            : "Démo activée.";
        return new ActivationResult(true, message);
    }

    /// <summary>
    /// « Réessayer » après une licence expirée : relance /v1/validate ; si le poste n'est plus activé, réactive avec
    /// la clé stockée (une licence prolongée redevient valide). Nécessite le serveur : pas de grâce hors-ligne ici.
    /// </summary>
    public async Task<ActivationResult> RetryStoredAsync()
    {
        var result = await CheckStoredAsync();
        if (result.Valid && !result.Offline)
        {
            return new ActivationResult(true, null);
        }

        if (result.Offline)
        {
            return new ActivationResult(false, LicenseMessages.ForReason("server_unreachable"));
        }

        if (result.Reason == "device_not_activated" && LoadStored() is { } stored)
        {
            return await ActivateAsync(stored.LicenseKey);
        }

        return new ActivationResult(false, LicenseMessages.ForReason(result.Reason));
    }

    /// <summary>Stocke la licence (dernière validation = maintenant). Renvoie un échec, ou null si tout va bien.</summary>
    private ActivationResult? Store(string licenseKey, LicenseInfo? license)
    {
        try
        {
            _storage.Save(Serialize(new StoredLicense
            {
                LicenseKey = licenseKey,
                ProductSlug = LicenseConfig.ProductSlug,
                DeviceId = _deviceId,
                LastValidatedAt = DateTimeOffset.UtcNow,
                License = license
            }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or CryptographicException)
        {
            return new ActivationResult(false, "La licence est valide mais n'a pas pu être enregistrée sur ce poste.");
        }

        SetCurrent(license, offline: false, graceUntil: null);
        return null;
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

    // ----- Bypass licence.ini piloté à distance -----

    private const int MaxNameLength = 120;

    /// <summary>
    /// Ordre « install_bypass » : crée licence.ini puis passe immédiatement en licence gratuite (sans redémarrage).
    /// Échec d'écriture (droits...) : ignoré en silence, licence normale conservée, nouvel essai au prochain contrôle.
    /// La licence stockée n'est pas effacée : elle est ignorée tant que licence.ini existe.
    /// </summary>
    private async Task InstallBypassAsync(string? bypassName)
    {
        var name = CleanName(bypassName);
        try
        {
            var path = FreeFilePath;
            if (!File.Exists(path))
            {
                var nl = Environment.NewLine;
                var content = "[licence]" + nl + (name != null ? "name=" + name + nl : "") + "type=free" + nl;
                File.WriteAllText(path, content, new UTF8Encoding(false));
            }
        }
        catch (Exception)
        {
            return;
        }

        IsFree = true;
        FreeName = name;
        IsOffline = false;
        GraceUntil = null;

        await PingBypassAsync(readFile: false);
    }

    /// <summary>
    /// Ping best-effort du mode bypass (au démarrage, puis toutes les 24 h). Ne bloque jamais, ignore toute erreur.
    /// Renvoie l'ordre du serveur (« remove_bypass ») ou null. Aucun appel si l'interrupteur global est désactivé.
    /// </summary>
    public async Task<string?> PingBypassAsync(bool readFile = true)
    {
        if (!LicenseConfig.Enabled || !IsFree)
        {
            return null;
        }

        try
        {
            if (readFile)
            {
                var read = await Task.Run(() => ReadFreeName(FreeFilePath));
                if (read != null)
                {
                    FreeName = read;
                }
            }

            return await _api.BypassPingAsync(_deviceId, _deviceName, FreeName);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Ordre « remove_bypass » : supprime licence.ini. Succès : prévient le serveur (best-effort) et quitte le mode
    /// gratuit (le flux normal de licence reprend). Échec (fichier verrouillé, droits) : rien d'autre, réessai au ping suivant.
    /// </summary>
    public async Task<bool> RemoveBypassAsync()
    {
        try
        {
            var path = FreeFilePath;
            File.Delete(path);
            if (File.Exists(path))
            {
                return false;
            }
        }
        catch (Exception)
        {
            return false;
        }

        try
        {
            await _api.BypassRemovedAsync(_deviceId);
        }
        catch (Exception)
        {
            // Au mieux : le serveur renverra de nouveau l'ordre ou l'administrateur constatera l'état.
        }

        if (_storage is NullStorage)
        {
            _storage = LicensePlatform.CreateStorage();
        }

        IsFree = false;
        FreeName = null;
        return true;
    }

    /// <summary>Extrait le nom du client d'un licence.ini : « name=... » sinon première ligne de texte libre. Jamais d'exception.</summary>
    public static string? ReadFreeName(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[8192];
            var count = stream.Read(buffer, 0, buffer.Length);
            return ParseFreeName(new UTF8Encoding(false, true).GetString(buffer, 0, count));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string? ParseFreeName(string text)
    {
        var lines = text.TrimStart('﻿').Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        foreach (var line in lines)
        {
            var eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].Trim().Equals("name", StringComparison.OrdinalIgnoreCase))
            {
                return CleanName(line[(eq + 1)..]);
            }
        }

        var free = lines.FirstOrDefault(l => l[0] is not ('[' or ';' or '#') && !l.Contains('='));
        return CleanName(free);
    }

    /// <summary>Nettoie un nom : trim, 120 caractères max ; null s'il est vide ou contient des caractères de contrôle.</summary>
    private static string? CleanName(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Any(char.IsControl))
        {
            return null;
        }

        return name.Length > MaxNameLength ? name[..MaxNameLength].TrimEnd() : name;
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
        "product_not_found" => "Ce produit est introuvable sur le serveur de licences.",
        "server_unreachable" or "server_unreachable_no_prior_validation" =>
            "Impossible de joindre le serveur de licences. Vérifiez votre connexion réseau et réessayez.",
        "offline_grace_expired" =>
            $"Le serveur de licences est injoignable et la période d'utilisation hors-ligne ({LicenseConfig.GraceDays} jours) est dépassée. Connectez-vous au réseau pour revalider la licence.",
        _ => "La licence a été refusée par le serveur."
    };
}
