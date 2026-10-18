using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using PortFlow.Identity.Application;
using PortFlow.Identity.Domain;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Web;

namespace PortFlow.Identity.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string Section = "Portflow:Security:Jwt";

    /// <summary>Claim <c>iss</c> = URL Keycloak mà trình duyệt thấy (KC_ISSUER).</summary>
    public string Issuer { get; set; } = "http://localhost:8180/realms/portflow";

    /// <summary>Lấy public key qua tên service nội bộ (KC_JWKS) – app không giữ khóa ký.</summary>
    public string JwksUri { get; set; } = "http://localhost:8180/realms/portflow/protocol/openid-connect/certs";

    public string[] Audiences { get; set; } = ["portflow-api"];

    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Xác thực bị từ chối sau khi token hợp lệ (user bị khóa, chưa có phòng ban...) – mang mã lỗi riêng.</summary>
internal sealed class PortflowAuthenticationException(IErrorCode code, string message) : Exception(message)
{
    public IErrorCode Code { get; } = code;
}

/// <summary>
/// Resource server: JWT do Keycloak ký (RS256), kiểm tra chữ ký qua JWKS, <c>iss</c>, <c>aud</c> chứa
/// <c>portflow-api</c>, <c>exp/nbf</c> (lệch giờ 30s); sau đó chuyển thành <see cref="TokenCurrentUser"/>.
/// </summary>
internal static class JwtAuthentication
{
    public const string JwksHttpClient = "portflow-jwks";
    private const string RealmAccess = "realm_access";

    public static void Configure(JwtBearerOptions options, JwtOptions jwt, IHttpClientFactory httpClients)
    {
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudiences = jwt.Audiences,
            ValidateLifetime = true,
            ClockSkew = jwt.ClockSkew,
            ValidateIssuerSigningKey = true,
            NameClaimType = "preferred_username",
        };
        options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(jwt.JwksUri,
            new JwksRetriever(jwt.Issuer), new HttpDocumentRetriever(httpClients.CreateClient(JwksHttpClient))
            {
                RequireHttps = false,
            });
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = TokenValidatedAsync,
            OnChallenge = ChallengeAsync,
        };
    }

    /// <summary>= PortflowJwtAuthenticationConverter: JWT → user PortFlow (JIT + permission từ DB).</summary>
    private static async Task TokenValidatedAsync(TokenValidatedContext context)
    {
        if (context.SecurityToken is not JsonWebToken jwt)
        {
            context.Fail("Unsupported token type");
            return;
        }
        var resolver = context.HttpContext.RequestServices.GetRequiredService<CurrentUserResolver>();
        TokenCurrentUser user;
        try
        {
            user = await resolver.ResolveAsync(Claims(jwt), RealmRoles(jwt), context.HttpContext.RequestAborted);
        }
        catch (DomainException rejected)
        {
            context.Fail(new PortflowAuthenticationException(rejected.Code, rejected.Message));
            return;
        }
        var identity = new ClaimsIdentity(JwtBearerDefaults.AuthenticationScheme, "preferred_username", ClaimTypes.Role);
        identity.AddClaim(new Claim("sub", user.Id.ToString()));
        identity.AddClaim(new Claim("preferred_username", user.Username));
        identity.AddClaims(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        identity.AddClaims(user.Permissions.Select(permission => new Claim(PortflowClaims.Permission, permission)));
        context.Principal = new ClaimsPrincipal(identity);
        context.HttpContext.Items[typeof(TokenCurrentUser)] = user;
    }

    /// <summary>= RestAuthenticationEntryPoint: 401/403 dạng ProblemDetail + header WWW-Authenticate.</summary>
    private static Task ChallengeAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        var http = context.HttpContext;
        switch (context.AuthenticateFailure)
        {
            case PortflowAuthenticationException rejected:
                return Problems.WriteAsync(http, Problems.Of(rejected.Code, rejected.Message));
            case SecurityTokenExpiredException:
                http.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\", error_description=\"expired\"";
                return Problems.WriteAsync(http, Problems.Of(AuthErrorCode.AUTH_TOKEN_EXPIRED, "Token has expired"));
            case { } invalid:
                http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(JwtAuthentication))
                    .LogDebug("Bearer token rejected: {Reason}", invalid.Message);
                http.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\", error_description=\"invalid\"";
                return Problems.WriteAsync(http, Problems.Of(AuthErrorCode.AUTH_INVALID_TOKEN, "Invalid token"));
            default:
                http.Response.Headers.WWWAuthenticate = "Bearer";
                return Problems.WriteAsync(http, Problems.Of(AuthErrorCode.AUTH_UNAUTHENTICATED, "Thiếu Bearer token"));
        }
    }

    private static TokenClaims Claims(JsonWebToken jwt) => new(
        jwt.Subject,
        Text(jwt, "preferred_username"),
        Text(jwt, "name"),
        Text(jwt, "email"),
        Text(jwt, "department"));

    internal static IReadOnlySet<string> RealmRoles(JsonWebToken jwt)
    {
        if (!jwt.TryGetPayloadValue<JsonElement>(RealmAccess, out var access) || access.ValueKind != JsonValueKind.Object
            || !access.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
        {
            return new HashSet<string>();
        }
        return roles.EnumerateArray().Select(r => r.ToString()).ToHashSet();
    }

    private static string? Text(JsonWebToken jwt, string claim) =>
        jwt.TryGetPayloadValue<string>(claim, out var value) ? value : null;

    /// <summary>Đọc JWKS (không dùng discovery) thành cấu hình ký cho JwtBearer; tự làm mới khi gặp <c>kid</c> lạ.</summary>
    private sealed class JwksRetriever(string issuer) : IConfigurationRetriever<OpenIdConnectConfiguration>
    {
        public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(string address, IDocumentRetriever retriever,
            CancellationToken cancel)
        {
            var keys = new JsonWebKeySet(await retriever.GetDocumentAsync(address, cancel));
            var configuration = new OpenIdConnectConfiguration { Issuer = issuer, JsonWebKeySet = keys };
            foreach (var key in keys.GetSigningKeys())
            {
                configuration.SigningKeys.Add(key);
            }
            return configuration;
        }
    }
}

public static class PortflowClaims
{
    public const string Permission = "permission";
}

internal sealed class ConfigureJwtBearer(IOptions<JwtOptions> jwt, IHttpClientFactory httpClients)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name == JwtBearerDefaults.AuthenticationScheme)
        {
            JwtAuthentication.Configure(options, jwt.Value, httpClients);
        }
    }

    public void Configure(JwtBearerOptions options) => Configure(JwtBearerDefaults.AuthenticationScheme, options);
}
