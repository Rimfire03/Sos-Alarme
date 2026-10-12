using System.Text;
using System.Text.Json;
using TomLine.Licensing;

namespace SosLan.Licensing;

/// <summary>
/// Stockage de la licence de SOS-LAN (élément opaque chiffré, format JSON historique <see cref="StoredLicense"/>)
/// exposé au système de licence partagé TomLine.Licensing. Le format stocké reste lisible par les versions précédentes.
/// </summary>
internal sealed class SosLanLicenseStore : ILicenseStore
{
    private readonly ILicenseStorage _storage;
    private readonly string _deviceId;

    public SosLanLicenseStore(ILicenseStorage storage, string deviceId)
    {
        _storage = storage;
        _deviceId = deviceId;
    }

    /// <summary>Vrai si le dernier enregistrement a échoué (droits, trousseau...) : la licence reste valide pour la
    /// session, mais n'est pas persistée.</summary>
    public bool LastSaveFailed { get; private set; }

    /// <summary>Licence stockée au format SOS-LAN (clé, poste, produit), ou null.</summary>
    public StoredLicense? LoadRaw()
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

    public TomLine.Licensing.StoredLicense? Load()
    {
        var s = LoadRaw();
        if (s == null)
        {
            return null;
        }

        // Licence enregistrée pour un autre produit ou un autre poste : jamais de grâce hors-ligne (comme avant).
        var bound = s.ProductSlug == LicenseConfig.ProductSlug && s.DeviceId == _deviceId;
        var info = s.License;
        return new TomLine.Licensing.StoredLicense
        {
            Key = s.LicenseKey,
            Type = info?.Type,
            CustomerName = info?.CustomerName,
            ExpiresAt = info?.ExpiresAt,
            MaxActivations = info?.MaxActivations,
            Features = FeaturesOf(info),
            LastValidatedAt = bound && s.LastValidatedAt != default ? s.LastValidatedAt : null,
            LastSeenAt = s.LastSeenAt
        };
    }

    public void Save(TomLine.Licensing.StoredLicense l)
    {
        var stored = new StoredLicense
        {
            LicenseKey = l.Key,
            ProductSlug = LicenseConfig.ProductSlug,
            DeviceId = _deviceId,
            LastValidatedAt = l.LastValidatedAt ?? DateTimeOffset.UtcNow,
            LastSeenAt = l.LastSeenAt,
            License = ToInfo(l)
        };

        try
        {
            _storage.Save(JsonSerializer.Serialize(stored, LicenseJson.Options));
            LastSaveFailed = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or System.Security.Cryptography.CryptographicException)
        {
            LastSaveFailed = true;
        }
    }

    public void Clear() => _storage.Delete();

    /// <summary>Licence du paquet partagé → licence SOS-LAN (affichage des Paramètres).</summary>
    public static LicenseInfo ToInfo(TomLine.Licensing.StoredLicense l) => new()
    {
        Key = l.Key,
        Product = LicenseConfig.ProductSlug,
        Type = l.Type,
        ExpiresAt = l.ExpiresAt,
        MaxActivations = l.MaxActivations ?? 0,
        Features = JsonSerializer.SerializeToElement(l.Features, LicenseJson.Options),
        CustomerName = l.CustomerName
    };

    private static List<string> FeaturesOf(LicenseInfo? info)
    {
        var list = new List<string>();
        if (info?.Features is { ValueKind: JsonValueKind.Array } array)
        {
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } f)
                {
                    list.Add(f);
                }
            }
        }

        return list;
    }
}

/// <summary>Identité du poste de SOS-LAN : SHA-256 de l'identifiant machine brut (Windows / macOS), inchangée.</summary>
internal sealed class SosLanDeviceIdentity : IDeviceIdentity
{
    private string? _id;

    public string DeviceId => _id ??= LicenseManager.ComputeDeviceId(LicensePlatform.CreateMachineIdProvider().GetRawMachineId());

    public string DeviceName => Environment.MachineName;
}

/// <summary>licence.ini de SOS-LAN (dossier d'installation, voir <see cref="LicensePlatform.GetInstallDirectory"/>) exposé
/// au système de licence partagé : emplacement, format et lecture du nom du client inchangés.</summary>
internal sealed class SosLanBypassFile : IBypassFile
{
    private static string Path => System.IO.Path.Combine(LicensePlatform.GetInstallDirectory(), LicenseConfig.FreeLicenseFileName);

    public bool Exists => File.Exists(Path);

    public string? ReadCustomerName() => LicenseManager.ReadFreeName(Path);

    public bool TryCreate(string? content)
    {
        try
        {
            if (!File.Exists(Path))
            {
                var nl = Environment.NewLine;
                var name = LicenseManager.CleanName(content);
                var text = "[licence]" + nl + (name != null ? "name=" + name + nl : "") + "type=free" + nl;
                File.WriteAllText(Path, text, new UTF8Encoding(false));
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool TryDelete()
    {
        try
        {
            File.Delete(Path);
            return !File.Exists(Path);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
