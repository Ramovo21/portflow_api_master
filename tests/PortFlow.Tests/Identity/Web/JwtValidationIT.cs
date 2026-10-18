using System.Net;
using PortFlow.Tests.Support;
using PortFlow.Tests.Support.Security;

namespace PortFlow.Tests.Identity.Web;

public class JwtValidationIT(IntegrationFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task MissingBearerIsUnauthenticatedProblemWithWwwAuthenticate()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Add("X-Correlation-Id", "cid-401");
        var response = await Anonymous.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
        Assert.Equal("application/problem+json", ContentType(response)?.MediaType);
        var body = await ReadJsonAsync(response);
        Assert.Equal("AUTH_UNAUTHENTICATED", body.GetProperty("errorCode").GetString());
        Assert.Equal("https://portflow.local/errors/AUTH_UNAUTHENTICATED", body.GetProperty("type").GetString());
        Assert.Equal("cid-401", body.GetProperty("correlationId").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Array, body.GetProperty("errors").ValueKind);
    }

    [Fact]
    public async Task ValidTokenIsAccepted()
    {
        var response = await ClientAs(SeedUser.SALES01).GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("sales01", (await ReadJsonAsync(response)).GetProperty("username").GetString());
    }

    [Fact]
    public Task TokenSignedByAnotherKeyIsInvalid() =>
        ExpectInvalidAsync("Bearer " + TestJwtIssuer.TokenSignedByForeignKey(SeedUser.SALES01));

    [Fact]
    public Task TamperedTokenIsInvalid()
    {
        var parts = TestJwtIssuer.Token(SeedUser.SALES01).Split('.');
        return ExpectInvalidAsync($"Bearer {parts[0]}.{parts[1][..^2]}AA.{parts[2]}");
    }

    [Fact]
    public Task WrongIssuerIsInvalid() =>
        ExpectInvalidAsync("Bearer " + TestJwtIssuer.Token(SeedUser.SALES01,
            spec => spec.Issuer = "http://localhost:8180/realms/other"));

    [Fact]
    public Task MissingPortflowApiAudienceIsInvalid() =>
        ExpectInvalidAsync("Bearer " + TestJwtIssuer.Token(SeedUser.SALES01, spec => spec.Audience = "account"));

    [Fact]
    public async Task ExpiredTokenHasDedicatedCode()
    {
        var past = DateTime.UtcNow.AddHours(-1);
        var token = TestJwtIssuer.Token(SeedUser.SALES01, spec =>
        {
            spec.IssuedAt = past;
            spec.Expires = past.AddSeconds(900);
        });
        var response = await ClientWith("Bearer " + token).GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("error=\"invalid_token\"", response.Headers.WwwAuthenticate.ToString());
        Assert.Equal("AUTH_TOKEN_EXPIRED", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task ClockSkewOfFewSecondsIsTolerated()
    {
        var now = DateTime.UtcNow;
        var token = TestJwtIssuer.Token(SeedUser.SALES01, spec =>
        {
            spec.IssuedAt = now.AddSeconds(-900);
            spec.Expires = now.AddSeconds(-10);
        });
        Assert.Equal(HttpStatusCode.OK, (await ClientWith("Bearer " + token).GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public Task GarbageTokenIsInvalid() => ExpectInvalidAsync("Bearer not.a.jwt");

    [Fact]
    public async Task PublicEndpointsNeedNoToken()
    {
        Assert.Equal(HttpStatusCode.OK, (await Anonymous.GetAsync("/actuator/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Anonymous.GetAsync("/api/health")).StatusCode);
    }

    private async Task ExpectInvalidAsync(string authorization)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        var response = await Anonymous.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("error=\"invalid_token\"", response.Headers.WwwAuthenticate.ToString());
        Assert.Equal("AUTH_INVALID_TOKEN", await ErrorCodeAsync(response));
    }
}
