using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace SosLan.Licensing;

/// <summary>Appels HTTP vers le serveur de licences (sans authentification : la clé fait office de secret).</summary>
public sealed class LicenseApiClient
{
    private static readonly HttpClient Http = new() { Timeout = LicenseConfig.RequestTimeout };

    public Task<LicenseApiResponse> ActivateAsync(string licenseKey, string deviceId, string deviceName) =>
        PostForLicenseAsync("/v1/activate", new { licenseKey, productSlug = LicenseConfig.ProductSlug, deviceId, deviceName });

    public Task<LicenseApiResponse> ValidateAsync(string licenseKey, string deviceId) =>
        PostForLicenseAsync("/v1/validate", new { licenseKey, productSlug = LicenseConfig.ProductSlug, deviceId });

    /// <summary>Libère le poste. Lève <see cref="LicenseServerUnreachableException"/> si le serveur ne répond pas.</summary>
    public async Task<bool> DeactivateAsync(string licenseKey, string deviceId)
    {
        var json = await PostAsync("/v1/deactivate", new { licenseKey, productSlug = LicenseConfig.ProductSlug, deviceId });
        return json.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
    }

    private async Task<LicenseApiResponse> PostForLicenseAsync(string path, object body)
    {
        var json = await PostAsync(path, body);
        try
        {
            return json.Deserialize<LicenseApiResponse>(LicenseJson.Options) ?? new LicenseApiResponse();
        }
        catch (JsonException ex)
        {
            throw new LicenseServerUnreachableException(ex);
        }
    }

    private static async Task<JsonElement> PostAsync(string path, object body)
    {
        try
        {
            using var response = await Http.PostAsJsonAsync(LicenseConfig.ServerUrl + path, body, LicenseJson.Options);

            // Erreur serveur (5xx) : indisponibilité, pas un refus de licence.
            if ((int)response.StatusCode >= 500)
            {
                throw new LicenseServerUnreachableException();
            }

            return await response.Content.ReadFromJsonAsync<JsonElement>(LicenseJson.Options);
        }
        catch (LicenseServerUnreachableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new LicenseServerUnreachableException(ex);
        }
    }
}
