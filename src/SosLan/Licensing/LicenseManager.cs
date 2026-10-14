using System.Security.Cryptography;
using System.Text;
using TomLine.Licensing;

namespace SosLan.Licensing;

public sealed record ActivationResult(bool Success, string? Message);

/// <summary>
/// Façade de licence de SOS-LAN, commune à Windows et macOS : conserve l'API et les messages existants ; la validation,
/// la grâce hors-ligne, les réponses signées et les ordres à distance sont portés par le paquet partagé TomLine.Licensing.
/// Les parties dépendantes de la plateforme (stockage, identifiant machine, licence.ini) sont injectées par adaptateurs.
/// </summary>
public sealed class LicenseManager
{
    private ILicenseStorage _storage;
    private string? _deviceIdValue;
    private readonly string _deviceName = Environment.MachineName;

    private LicenseClient? _client;
    private LicenseService? _service;
    private SosLanLicenseStore? _store;

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

    // ----- Composition du système partagé (créé à la demande) -----

    private LicenseOptions BuildOptions() => new()
    {
        ProductSlug = LicenseConfig.ProductSlug,
        StoreName = "SosLan",   // non utilisé : le stockage est celui de SOS-LAN (SosLanLicenseStore)
        BaseUrl = LicenseConfig.ServerUrl,
        // Clé publique du serveur de licences (vérification seule). Un faux serveur de développement (build DEBUG avec
        // SOSLAN_LICENSE_URL) ne signe pas : la signature n'est exigée que pour le serveur de production.
        PublicKeys = new[] { LicenseConfig.ServerPublicKey },
        RequireSignature = LicenseConfig.RequireSignedResponses,
        OfflineGrace = TimeSpan.FromDays(LicenseConfig.GraceDays),
        // Un refus (licence expirée...) ne supprime pas la licence : le flux d'écrans décide (voir LicenseFlow) ;
        // seules « révoquée » et « introuvable » l'effacent d'emblée.
        KeepLicenseOnRecoverableRefusal = true
    };

    private LicenseService Service
    {
        get
        {
            if (_service != null)
            {
                return _service;
            }

            var device = new SosLanDeviceIdentity();
            _client = new LicenseClient(BuildOptions());
            _store = new SosLanLicenseStore(_storage, _deviceId);
            _service = new LicenseService(_client, device, _store, new SosLanBypassFile());
            return _service;
        }
    }

    private LicenseClient Client
    {
        get
        {
            _ = Service;
            return _client!;
        }
    }

    private SosLanLicenseStore Store
    {
        get
        {
            _ = Service;
            return _store!;
        }
    }

    public bool HasStoredLicense => Store.LoadRaw() != null;

    /// <summary>Ligne Â« Postes : 2/5 Â» (ou Â« 1/âˆž Â») de la licence en cours, ou null quand le serveur ne l'a pas renvoyÃ©e.</summary>
    public string? PostsLine => IsFree ? null : LicenseFooter.PostsLine(_service?.State.License);

    /// <summary>Informations client demandées par le serveur (null si rien, ou en licence gratuite).</summary>
    public ProfileRequest? ProfileRequest => IsFree ? null : _service?.ProfileRequest;

    /// <summary>Envoie les informations client au serveur de licences (licence stockée de ce poste).</summary>
    public Task<(bool Done, string? Reason, IReadOnlyList<string> Fields)> SubmitProfileAsync(DemoRequestProfile profile) =>
        Service.SubmitProfileAsync(profile);

    // ----- Contrôle (port de checkLicense) -----

    /// <summary>Valide en ligne la licence stockée ; si le serveur est injoignable, applique la grâce hors-ligne.</summary>
    public async Task<LicenseCheckResult> CheckStoredAsync()
    {
        if (Store.LoadRaw() == null)
        {
            return new LicenseCheckResult(false, false, "no_license", null, null);
        }

        var state = await Service.RevalidateAsync();
        switch (state.Status)
        {
            case LicenseStatus.Valid:
                SetCurrent(state.License, offline: false, graceUntil: null);
                return new LicenseCheckResult(true, false, null, CurrentLicense, null);

            case LicenseStatus.OfflineGrace:
                SetCurrent(state.License, offline: true, graceUntil: state.GraceUntil);
                return new LicenseCheckResult(true, true, null, state.License is { } g ? SosLanLicenseStore.ToInfo(g) : null, state.GraceUntil);

            case LicenseStatus.Bypass:
                // Ordre « install_bypass » exécuté par le service : licence gratuite immédiatement, sans redémarrage.
                IsFree = true;
                FreeName = ReadFreeName(FreeFilePath);
                IsOffline = false;
                GraceUntil = null;
                return new LicenseCheckResult(true, false, null, CurrentLicense, null);

            case LicenseStatus.NoLicense:
                return new LicenseCheckResult(false, false, "no_license", null, null);

            default:
                return LockedResult(state);
        }
    }

    private static LicenseCheckResult LockedResult(LicenseState state) => state.Reason switch
    {
        // Pas de réponse du serveur : jamais un refus de licence (la licence stockée est conservée).
        "no_prior_validation" => new LicenseCheckResult(false, true, "server_unreachable_no_prior_validation", null, null),
        "offline_grace_expired" => new LicenseCheckResult(false, true, "offline_grace_expired", null, null),
        "clock_tampered" => new LicenseCheckResult(false, true, "clock_tampered", null, null),
        // Refus explicite (révoquée, expirée, poste non activé...) : réponse ferme, jamais de grâce.
        _ => new LicenseCheckResult(false, false, state.Reason, null, null)
    };

    // ----- Activation / changement / suppression -----

    /// <summary>Active une clé sur ce poste et la stocke. En cas d'échec, le stockage existant reste intact.</summary>
    public async Task<ActivationResult> ActivateAsync(string licenseKey)
    {
        licenseKey = licenseKey.Trim();
        if (licenseKey.Length == 0)
        {
            return new ActivationResult(false, "Saisissez votre clé de licence.");
        }

        var state = await Service.ActivateAsync(licenseKey);
        if (state.Status != LicenseStatus.Valid)
        {
            // Limite de postes atteinte avec compteur : Â« Limite atteinte : 2/2 postes utilisÃ©s Â».
            if (state.Reason == "activation_limit_reached" && state.ActivationsText is not null)
            {
                return new ActivationResult(false, LicenseReasons.DescribeRefusal(state));
            }

            return new ActivationResult(false, LicenseMessages.ForReason(MapNetwork(state.Reason)));
        }

        // Compteur de postes connu : Â« Licence activÃ©e (poste 2/5) Â» ; sinon aucun message (comportement inchangÃ©).
        return Stored(state.License)
            ?? new ActivationResult(true, state.License?.ActivationsText is not null ? LicenseFooter.ActivationMessage(state) : null);
    }

    /// <summary>
    /// Demande une démo pour ce poste (aucune saisie de clé). Le serveur l'active directement : la clé reçue est
    /// stockée telle quelle, sans jamais être affichée, copiée ni journalisée.
    /// </summary>
    public async Task<ActivationResult> RequestDemoAsync(DemoRequestProfile profile)
    {
        var state = await Service.RequestDemoAsync(profile);
        if (state.Status != LicenseStatus.Valid)
        {
            // Messages propres à la démo (le serveur répond 403 license_expired / license_revoked pour ce poste).
            return new ActivationResult(false, state.Reason switch
            {
                "license_expired" => "La démo de ce poste est terminée. Saisissez une clé de licence.",
                "license_revoked" => "Cette démo a été désactivée. Saisissez une clé de licence.",
                "invalid_response" => "La démo n'a pas pu être activée. Réessayez ou saisissez une clé de licence.",
                _ => LicenseMessages.ForReason(MapNetwork(state.Reason))
            });
        }

        var failure = Stored(state.License);
        if (failure != null)
        {
            return failure;
        }

        var message = state.License?.ExpiresAt is { } expiresAt
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

        if (result.Reason == "device_not_activated" && Store.LoadRaw() is { } stored)
        {
            return await ActivateAsync(stored.LicenseKey);
        }

        return new ActivationResult(false, LicenseMessages.ForReason(result.Reason));
    }

    /// <summary>Enregistre la licence de la session. Renvoie un échec si elle n'a pas pu être persistée, sinon null.</summary>
    private ActivationResult? Stored(TomLine.Licensing.StoredLicense? license)
    {
        if (Store.LastSaveFailed)
        {
            return new ActivationResult(false, "La licence est valide mais n'a pas pu être enregistrée sur ce poste.");
        }

        SetCurrent(license, offline: false, graceUntil: null);
        return null;
    }

    private static string? MapNetwork(string? reason) => reason == "network" ? "server_unreachable" : reason;

    /// <summary>
    /// Remplace la licence : la nouvelle clé est d'abord activée ; seulement en cas de succès l'ancienne est
    /// désactivée côté serveur. En cas d'échec, l'ancienne licence reste intacte.
    /// </summary>
    public async Task<ActivationResult> ChangeAsync(string newLicenseKey)
    {
        var previous = Store.LoadRaw();
        var result = await ActivateAsync(newLicenseKey);

        if (result.Success && previous != null && !string.Equals(previous.LicenseKey, newLicenseKey.Trim(), StringComparison.Ordinal))
        {
            try
            {
                await Client.DeactivateAsync(previous.LicenseKey, _deviceId);
            }
            catch (LicenseNetworkException)
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
        var stored = Store.LoadRaw();
        if (stored != null)
        {
            try
            {
                await Client.DeactivateAsync(stored.LicenseKey, _deviceId);
            }
            catch (LicenseNetworkException)
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

            var reply = await Client.BypassPingAsync(_deviceId, _deviceName, FreeName);
            return reply.Command;
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
            await Client.BypassRemovedAsync(_deviceId);
        }
        catch (Exception)
        {
            // Au mieux : le serveur renverra de nouveau l'ordre ou l'administrateur constatera l'état.
        }

        if (_storage is NullStorage)
        {
            _storage = LicensePlatform.CreateStorage();
        }

        // Le service est recomposé avec le vrai stockage au prochain besoin (état « gratuit » abandonné).
        _service = null;
        _client = null;
        _store = null;
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
    internal static string? CleanName(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Any(char.IsControl))
        {
            return null;
        }

        return name.Length > MaxNameLength ? name[..MaxNameLength].TrimEnd() : name;
    }

    // ----- Utilitaires -----

    private void SetCurrent(TomLine.Licensing.StoredLicense? license, bool offline, DateTimeOffset? graceUntil)
    {
        CurrentLicense = license == null ? null : SosLanLicenseStore.ToInfo(license);
        IsOffline = offline;
        GraceUntil = graceUntil;
    }

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
        "clock_tampered" =>
            "L'horloge de ce poste est en retard sur la dernière validation de la licence. Corrigez l'heure du poste et connectez-vous au réseau.",
        _ => "La licence a été refusée par le serveur."
    };
}
