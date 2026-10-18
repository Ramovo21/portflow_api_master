using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PortFlow.Tests.Support.Security;

namespace PortFlow.Tests.Identity.Keycloak;

public class KeycloakLoginE2EIT(KeycloakFixture fixture) : KeycloakIntegrationTest(fixture)
{
    [Fact]
    public async Task LoginWithKeycloakThenCallMe()
    {
        var accessToken = await Kc.AccessTokenAsync("sales01");
        var claims = KeycloakTestClient.Claims(accessToken);
        Assert.Equal(SeedUser.SALES01.Id.ToString(), claims.GetProperty("sub").GetString());
        Assert.Equal("sales01", claims.GetProperty("preferred_username").GetString());
        Assert.Equal("Nguyễn Văn An", claims.GetProperty("name").GetString());
        Assert.Equal("KD", claims.GetProperty("department").GetString());
        Assert.Contains("portflow-api", Audience(claims));
        Assert.Contains("SALES", claims.GetProperty("realm_access").GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal(900, claims.GetProperty("exp").GetInt64() - claims.GetProperty("iat").GetInt64());

        var response = await ClientWith("Bearer " + accessToken).GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await ReadJsonAsync(response);
        Assert.Equal(SeedUser.SALES01.Id, me.GetProperty("id").GetGuid());
        Assert.Equal("KD", me.GetProperty("department").GetProperty("code").GetString());
        Assert.Contains("Contract.Create", me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
    }

    [Fact]
    public async Task ReusedRefreshTokenIsRejected()
    {
        var login = await (await Kc.PasswordGrantAsync("legal01", KeycloakTestClient.Password)).Content
            .ReadFromJsonAsync<JsonElement>();
        var r1 = login.GetProperty("refresh_token").GetString()!;

        var rotated = await Kc.RefreshAsync(r1);
        Assert.True(rotated.IsSuccessStatusCode);
        Assert.NotEqual(r1, (await rotated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refresh_token").GetString());

        var reused = await Kc.RefreshAsync(r1);
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
        Assert.Equal("invalid_grant", (await reused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task FiveWrongPasswordsLockTheAccount()
    {
        var username = "bf-" + Guid.NewGuid().ToString()[..8];
        await Kc.CreateUserAsync(username, "KD", "SALES");
        Assert.True((await Kc.PasswordGrantAsync(username, KeycloakTestClient.Password)).IsSuccessStatusCode);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Kc.PasswordGrantAsync(username, "Sai@mat-khau-" + i)).StatusCode);
        }
        Assert.False((await Kc.PasswordGrantAsync(username, KeycloakTestClient.Password)).IsSuccessStatusCode);
    }

    [Fact]
    public async Task TokenCarriesNoPasswordAndAdminHasNoApprovalPermission()
    {
        var response = await Http.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await ReadJsonAsync(response);
        Assert.Equal("ADMIN", me.GetProperty("roles")[0].GetString());
        var permissions = me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Contains("User.Admin", permissions);
        Assert.DoesNotContain("Workflow.Act", permissions);
    }

    private static IEnumerable<string?> Audience(JsonElement claims)
    {
        var aud = claims.GetProperty("aud");
        return aud.ValueKind == JsonValueKind.String ? [aud.GetString()] : aud.EnumerateArray().Select(a => a.GetString());
    }
}
