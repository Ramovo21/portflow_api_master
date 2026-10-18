using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PortFlow.Identity.Domain;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Time;

namespace PortFlow.Identity.Infrastructure.Keycloak;

public sealed class KeycloakOptions
{
    public const string Section = "Portflow:Keycloak";

    public string ServerUrl { get; set; } = "http://localhost:8180";

    public string Realm { get; set; } = "portflow";

    public string ClientId { get; set; } = "portflow-backend";

    public string ClientSecret { get; set; } = "portflow-backend-secret";

    public int PageSize { get; set; } = 100;
}

/// <summary>Đọc user từ Keycloak Admin REST API bằng service account <c>portflow-backend</c> (client credentials).</summary>
public sealed class KeycloakAdminClient(
    HttpClient http,
    IOptions<KeycloakOptions> options,
    IBusinessClock clock,
    ILogger<KeycloakAdminClient> log)
{
    public const string HttpClientName = "keycloak-admin";

    public async Task<IReadOnlyList<KeycloakUserSnapshot>> UsersAsync(string syncSource, CancellationToken ct)
    {
        var settings = options.Value;
        try
        {
            var token = await ServiceAccountTokenAsync(settings, ct);
            var result = new List<KeycloakUserSnapshot>();
            for (var first = 0; ; first += settings.PageSize)
            {
                var page = await GetAsync<List<JsonElement>>(token,
                    $"{settings.ServerUrl}/admin/realms/{settings.Realm}/users?first={first}&max={settings.PageSize}&briefRepresentation=false",
                    ct) ?? [];
                foreach (var user in page.Where(u => !IsServiceAccount(u)))
                {
                    result.Add(await SnapshotAsync(settings, user, token, syncSource, ct));
                }
                if (page.Count < settings.PageSize)
                {
                    return result;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or InvalidOperationException or FormatException or KeyNotFoundException)
        {
            log.LogWarning(ex, "Keycloak Admin API at {ServerUrl} failed", settings.ServerUrl);
            throw new DomainException(AuthErrorCode.AUTH_IDP_UNAVAILABLE,
                "Failed to sync users from Keycloak: " + ex.Message);
        }
    }

    private async Task<string> ServiceAccountTokenAsync(KeycloakOptions settings, CancellationToken ct)
    {
        using var response = await http.PostAsync(
            $"{settings.ServerUrl}/realms/{settings.Realm}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = settings.ClientId,
                ["client_secret"] = settings.ClientSecret,
            }), ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (!body.TryGetProperty("access_token", out var token) || token.ValueKind != JsonValueKind.String)
        {
            throw new DomainException(AuthErrorCode.AUTH_IDP_UNAVAILABLE, "Keycloak did not return an access token");
        }
        return token.GetString()!;
    }

    private async Task<KeycloakUserSnapshot> SnapshotAsync(KeycloakOptions settings, JsonElement user, string token,
        string syncSource, CancellationToken ct)
    {
        var id = Guid.Parse(user.GetProperty("id").GetString()!);
        var username = user.GetProperty("username").GetString()!;
        return new KeycloakUserSnapshot(id, username, FullName(user, username), Text(user, "email"), Department(user),
            user.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True,
            await RealmRolesAsync(settings, id, token, ct), clock.Now(), syncSource);
    }

    private async Task<IReadOnlySet<string>> RealmRolesAsync(KeycloakOptions settings, Guid userId, string token,
        CancellationToken ct)
    {
        var roles = await GetAsync<List<JsonElement>>(token,
            $"{settings.ServerUrl}/admin/realms/{settings.Realm}/users/{userId}/role-mappings/realm/composite", ct);
        return (roles ?? []).Select(r => r.GetProperty("name").GetString()!).ToHashSet();
    }

    private async Task<T?> GetAsync<T>(string token, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct);
    }

    private static bool IsServiceAccount(JsonElement user) =>
        (user.TryGetProperty("serviceAccountClientLink", out var link) && link.ValueKind != JsonValueKind.Null)
        || (Text(user, "username")?.StartsWith("service-account-", StringComparison.Ordinal) ?? false);

    private static string FullName(JsonElement user, string username)
    {
        var name = $"{Text(user, "firstName")} {Text(user, "lastName")}".Trim();
        return name.Length == 0 ? username : name;
    }

    private static string? Text(JsonElement user, string property) =>
        user.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Department(JsonElement user) =>
        user.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object
        && attributes.TryGetProperty("department", out var values) && values.ValueKind == JsonValueKind.Array
        && values.GetArrayLength() > 0
            ? values[0].ToString()
            : null;
}
