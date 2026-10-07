using System.Text.Json;
using System.Text.Json.Serialization;

namespace SosLan.Licensing;

/// <summary>Données de licence renvoyées par le serveur.</summary>
public sealed class LicenseInfo
{
    public string? Key { get; set; }
    public string? Product { get; set; }
    public string? Type { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public int MaxActivations { get; set; }
    public JsonElement? Features { get; set; }
    public string? CustomerName { get; set; }
}

/// <summary>Contenu de l'unique élément stocké (chiffré) : jamais écrit en clair.</summary>
public sealed class StoredLicense
{
    public string LicenseKey { get; set; } = "";
    public string ProductSlug { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public DateTimeOffset LastValidatedAt { get; set; }
    public LicenseInfo? License { get; set; }
}

/// <summary>Réponse brute du serveur pour activate / validate.</summary>
public sealed class LicenseApiResponse
{
    public bool Valid { get; set; }
    public string? Reason { get; set; }
    public LicenseInfo? License { get; set; }
}

/// <summary>Résultat d'un contrôle (port de checkLicense du client de référence).</summary>
public sealed record LicenseCheckResult(
    bool Valid,
    bool Offline,
    string? Reason,
    LicenseInfo? License,
    DateTimeOffset? GraceUntil);

public static class LicenseJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>Le serveur de licences n'a pas répondu (réseau, délai dépassé, erreur 5xx, réponse illisible).</summary>
public sealed class LicenseServerUnreachableException : Exception
{
    public LicenseServerUnreachableException(Exception? inner = null)
        : base("Serveur de licences injoignable.", inner)
    {
    }
}
