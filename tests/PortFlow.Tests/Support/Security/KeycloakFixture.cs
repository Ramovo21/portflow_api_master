using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.DependencyInjection;

namespace PortFlow.Tests.Support.Security;

/// <summary>
/// = AbstractKeycloakIntegrationTest: thêm Keycloak thật (import <c>infra/keycloak/portflow-realm.json</c>);
/// app xác thực token Keycloak qua JWKS thật và đồng bộ user qua Admin API. Chậm → Category=Keycloak.
/// </summary>
public sealed class KeycloakFixture : IntegrationFixture
{
    public IContainer Keycloak { get; } = new ContainerBuilder("quay.io/keycloak/keycloak:26.0.7")
        .WithCommand("start-dev", "--import-realm")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
        .WithEnvironment("KC_HEALTH_ENABLED", "true")
        .WithResourceMapping(new FileInfo(Path.Combine(AppContext.BaseDirectory, "keycloak", "portflow-realm.json")),
            "/opt/keycloak/data/import/")
        .WithPortBinding(8080, true)
        .WithPortBinding(9000, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9000).ForPath("/health/ready")))
        .Build();

    public string BaseUrl => $"http://{Keycloak.Hostname}:{Keycloak.GetMappedPublicPort(8080)}";

    public KeycloakTestClient Kc => new(BaseUrl);

    protected override Task StartExtraAsync() => Keycloak.StartAsync();

    protected override Dictionary<string, string?> Settings()
    {
        var settings = base.Settings();
        settings["Portflow:Security:Jwt:Issuer"] = BaseUrl + "/realms/portflow";
        settings["Portflow:Security:Jwt:JwksUri"] = BaseUrl + "/realms/portflow/protocol/openid-connect/certs";
        settings["Portflow:Keycloak:ServerUrl"] = BaseUrl;
        settings["Portflow:Keycloak:ClientSecret"] = "portflow-backend-secret";
        return settings;
    }

    // JWKS thật từ Keycloak (không thay handler)
    protected override void ConfigureServices(IServiceCollection services)
    {
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await Keycloak.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class KeycloakCollection : ICollectionFixture<KeycloakFixture>
{
    public const string Name = "keycloak";
}

/// <summary>Gọi Keycloak như SPA/admin: password grant (client dev), refresh, tạo/sửa/xóa user.</summary>
public sealed class KeycloakTestClient(string baseUrl)
{
    public const string Password = "Portflow@123";

    private static readonly HttpClient Http = new();

    public Task<HttpResponseMessage> PasswordGrantAsync(string username, string password) =>
        TokenRequestAsync(new() { ["grant_type"] = "password", ["client_id"] = "portflow-dev-cli",
            ["username"] = username, ["password"] = password });

    public Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        TokenRequestAsync(new() { ["grant_type"] = "refresh_token", ["client_id"] = "portflow-dev-cli",
            ["refresh_token"] = refreshToken });

    public async Task<string> AccessTokenAsync(string username)
    {
        var response = await PasswordGrantAsync(username, Password);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Login {username} failed: {await response.Content.ReadAsStringAsync()}");
        }
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;
    }

    public static JsonElement Claims(string jwt)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement;
    }

    public async Task<Guid> CreateUserAsync(string username, string? department, params string[] realmRoles)
    {
        var user = new Dictionary<string, object>
        {
            ["username"] = username,
            ["enabled"] = true,
            ["firstName"] = "Người",
            ["lastName"] = username,
            ["email"] = username + "@abc.vn",
            ["emailVerified"] = true,
            ["attributes"] = department is null ? new Dictionary<string, object>()
                : new Dictionary<string, object> { ["department"] = new[] { department } },
            ["credentials"] = new[] { new { type = "password", value = Password, temporary = false } },
        };
        var created = await AdminAsync(HttpMethod.Post, "/admin/realms/portflow/users", user);
        if (created.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"Create user failed: {created.StatusCode}");
        }
        var location = created.Headers.Location!.ToString();
        var id = Guid.Parse(location[(location.LastIndexOf('/') + 1)..]);
        await AddRealmRolesAsync(id, realmRoles);
        return id;
    }

    public async Task AddRealmRolesAsync(Guid userId, params string[] realmRoles)
    {
        if (realmRoles.Length == 0)
        {
            return;
        }
        var roles = new List<JsonElement>();
        foreach (var role in realmRoles)
        {
            var response = await AdminAsync(HttpMethod.Get, $"/admin/realms/portflow/roles/{role}", null);
            roles.Add(await response.Content.ReadFromJsonAsync<JsonElement>());
        }
        await AdminAsync(HttpMethod.Post, $"/admin/realms/portflow/users/{userId}/role-mappings/realm", roles);
    }

    public Task SetEnabledAsync(Guid userId, bool enabled) =>
        AdminAsync(HttpMethod.Put, $"/admin/realms/portflow/users/{userId}", new { enabled });

    public Task DeleteUserAsync(Guid userId) => AdminAsync(HttpMethod.Delete, $"/admin/realms/portflow/users/{userId}", null);

    private async Task<HttpResponseMessage> AdminAsync(HttpMethod method, string path, object? body)
    {
        var token = await Http.PostAsync($"{baseUrl}/realms/master/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password", ["client_id"] = "admin-cli", ["username"] = "admin", ["password"] = "admin",
            }));
        var accessToken = (await token.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString();
        var request = new HttpRequestMessage(method, baseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }
        return await Http.SendAsync(request);
    }

    private Task<HttpResponseMessage> TokenRequestAsync(Dictionary<string, string> form) =>
        Http.PostAsync($"{baseUrl}/realms/portflow/protocol/openid-connect/token", new FormUrlEncodedContent(form));
}
