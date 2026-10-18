using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace PortFlow.Tests.Support.Security;

/// <summary>
/// Ký JWT giống Keycloak (RS256, iss/aud/realm_access/department) để test không cần Keycloak thật.
/// JWKS được phục vụ bởi <see cref="JwksHandler"/> thay cho endpoint <c>/certs</c>.
/// </summary>
public static class TestJwtIssuer
{
    public const string Issuer = "http://keycloak.test/realms/portflow";
    public const string Audience = "portflow-api";
    public const string JwksUri = "http://keycloak.test/realms/portflow/protocol/openid-connect/certs";

    private static readonly RSA Key = RSA.Create(2048);
    private static readonly RSA ForeignKey = RSA.Create(2048);
    private const string KeyId = "test-key";

    public static string JwksJson { get; } = BuildJwks(Key);

    public static string Bearer(SeedUser user) => "Bearer " + Token(user);

    public static string Token(SeedUser user, Action<TokenSpec>? customize = null) => Sign(Key, spec =>
    {
        spec.Claims["sub"] = user.Id.ToString();
        spec.Claims["preferred_username"] = user.Username;
        spec.Claims["name"] = user.FullName;
        spec.Claims["email"] = user.Email;
        spec.Claims["department"] = user.Department;
        spec.Claims["realm_access"] = RealmAccess(user.Roles);
        customize?.Invoke(spec);
    });

    public static string BearerFor(Guid id, string username, string? department, params string[] roles) =>
        "Bearer " + Sign(Key, spec =>
        {
            spec.Claims["sub"] = id.ToString();
            spec.Claims["preferred_username"] = username;
            spec.Claims["name"] = "Test " + username;
            spec.Claims["email"] = username + "@abc.vn";
            spec.Claims["realm_access"] = RealmAccess(roles);
            if (department is not null)
            {
                spec.Claims["department"] = department;
            }
        });

    public static string TokenSignedByForeignKey(SeedUser user) => Sign(ForeignKey, spec =>
    {
        spec.Claims["sub"] = user.Id.ToString();
        spec.Claims["preferred_username"] = user.Username;
        spec.Claims["department"] = user.Department;
        spec.Claims["realm_access"] = RealmAccess(user.Roles);
    });

    public static List<string> RolesWithDefaults(IEnumerable<string> roles) =>
        [.. roles, "default-roles-portflow", "offline_access"];

    private static Dictionary<string, object> RealmAccess(IEnumerable<string> roles) =>
        new() { ["roles"] = RolesWithDefaults(roles) };

    private static string Sign(RSA key, Action<TokenSpec> customize)
    {
        var now = DateTime.UtcNow;
        var spec = new TokenSpec { IssuedAt = now, Expires = now.AddSeconds(900) };
        spec.Claims["jti"] = Guid.NewGuid().ToString();
        spec.Claims["azp"] = "portflow-web";
        customize(spec);
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = spec.Issuer,
            Audience = spec.Audience,
            IssuedAt = spec.IssuedAt,
            NotBefore = spec.IssuedAt,
            Expires = spec.Expires,
            Claims = spec.Claims,
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(key) { KeyId = KeyId },
                SecurityAlgorithms.RsaSha256),
        });
    }

    private static string BuildJwks(RSA key)
    {
        var parameters = key.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA", use = "sig", alg = "RS256", kid = KeyId,
                    n = Base64UrlEncoder.Encode(parameters.Modulus), e = Base64UrlEncoder.Encode(parameters.Exponent),
                },
            },
        });
    }

    public sealed class TokenSpec
    {
        public string Issuer { get; set; } = TestJwtIssuer.Issuer;

        public string Audience { get; set; } = TestJwtIssuer.Audience;

        public DateTime IssuedAt { get; set; }

        public DateTime Expires { get; set; }

        public Dictionary<string, object> Claims { get; } = new();
    }

    /// <summary>Trả JWKS của khóa test cho HttpClient <c>portflow-jwks</c>.</summary>
    public sealed class JwksHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JwksJson, Encoding.UTF8, "application/json"),
            });
    }
}
