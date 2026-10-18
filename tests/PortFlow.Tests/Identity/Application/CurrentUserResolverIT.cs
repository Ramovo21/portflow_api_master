using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PortFlow.Identity.Application;
using PortFlow.Tests.Support;
using PortFlow.Tests.Support.Security;

namespace PortFlow.Tests.Identity.Application;

public class CurrentUserResolverIT(IntegrationFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task MeReturnsProfileRolesPermissionsScopeAndDepartment()
    {
        var response = await ClientAs(SeedUser.SALESMGR).GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await ReadJsonAsync(response);
        Assert.Equal(SeedUser.SALESMGR.Id, me.GetProperty("id").GetGuid());
        Assert.Equal("Trần Thị Bình", me.GetProperty("fullName").GetString());
        Assert.Equal("KD", me.GetProperty("department").GetProperty("code").GetString());
        Assert.Equal("Phòng Kinh doanh", me.GetProperty("department").GetProperty("name").GetString());
        Assert.Equal(["SALES_MANAGER"], Strings(me, "roles"));
        Assert.Equal("DEPARTMENT", me.GetProperty("dataScope").GetString());
        Assert.Contains("Contract.Cancel", Strings(me, "permissions"));
        Assert.Contains("Workflow.Act", Strings(me, "permissions"));
        Assert.Equal(System.Text.Json.JsonValueKind.Array, me.GetProperty("actingFor").ValueKind);
    }

    [Fact]
    public async Task LegalHasAllScope()
    {
        var me = await ReadJsonAsync(await ClientAs(SeedUser.LEGAL01).GetAsync("/api/v1/me"));
        Assert.Equal("ALL", me.GetProperty("dataScope").GetString());
    }

    [Fact]
    public async Task AdminHasNoApprovalPermission()
    {
        var response = await ClientAs(SeedUser.ADMIN).GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var permissions = Strings(await ReadJsonAsync(response), "permissions");
        Assert.Contains("User.Admin", permissions);
        Assert.DoesNotContain("Workflow.Act", permissions);
    }

    [Fact]
    public async Task FirstRequestOfUnknownUserCreatesMirrorJustInTime()
    {
        var id = Guid.NewGuid();
        var response = await ClientWith(TestJwtIssuer.BearerFor(id, "jit-" + id.ToString()[..8], "PC", "LEGAL"))
            .GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await ReadJsonAsync(response);
        Assert.Equal("PC", me.GetProperty("department").GetProperty("code").GetString());
        Assert.Equal(["LEGAL"], Strings(me, "roles"));

        Assert.Equal("JIT", await ScalarAsync<string>("SELECT sync_source FROM identity.app_user WHERE id = @id", new { id }));
        Assert.Equal(["LEGAL"], await ListAsync<string>("SELECT role_code FROM identity.user_role WHERE user_id = @id", new { id }));
        Assert.Equal(1, await EventsForAsync("UserProvisioned", id));
    }

    [Fact]
    public async Task ChangedClaimsUpdateMirror()
    {
        var id = Guid.NewGuid();
        var username = "chg-" + id.ToString()[..8];
        Assert.Equal(HttpStatusCode.OK,
            (await ClientWith(TestJwtIssuer.BearerFor(id, username, "KD", "SALES")).GetAsync("/api/v1/me")).StatusCode);

        var response = await ClientWith(TestJwtIssuer.BearerFor(id, username, "PC", "LEGAL")).GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("PC", (await ReadJsonAsync(response)).GetProperty("department").GetProperty("code").GetString());
        Assert.Equal(["LEGAL"], await ListAsync<string>("SELECT role_code FROM identity.user_role WHERE user_id = @id", new { id }));
        Assert.Equal(1, await EventsForAsync("UserRolesChanged", id));
    }

    [Fact]
    public async Task InactiveUserIsForbiddenEvenWithValidToken()
    {
        var id = Guid.NewGuid();
        var client = ClientWith(TestJwtIssuer.BearerFor(id, "off-" + id.ToString()[..8], "KD", "SALES"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me")).StatusCode);

        await ExecuteAsync("UPDATE identity.app_user SET active = false WHERE id = @id", new { id });
        Fixture.App.Services.GetRequiredService<CurrentUserCache>().Evict(id);

        var response = await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("AUTH_USER_INACTIVE", await ErrorCodeAsync(response));
        Assert.False(await ScalarAsync<bool>("SELECT active FROM identity.app_user WHERE id = @id", new { id }));
    }

    [Fact]
    public async Task UserWithoutDepartmentIsNotProvisioned()
    {
        var id = Guid.NewGuid();
        var response = await ClientWith(TestJwtIssuer.BearerFor(id, "nodept-" + id.ToString()[..8], null, "LEGAL"))
            .GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("AUTH_USER_NOT_PROVISIONED", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task UnknownDepartmentCodeIsNotProvisioned()
    {
        var id = Guid.NewGuid();
        var response = await ClientWith(TestJwtIssuer.BearerFor(id, "baddept-" + id.ToString()[..8], "NOPE", "LEGAL"))
            .GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("AUTH_USER_NOT_PROVISIONED", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task RecreatedKeycloakAccountWithSameUsernameTakesOverUsername()
    {
        var username = "again-" + Guid.NewGuid().ToString()[..8];
        var oldId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.OK,
            (await ClientWith(TestJwtIssuer.BearerFor(oldId, username, "KD", "SALES")).GetAsync("/api/v1/me")).StatusCode);

        var response = await ClientWith(TestJwtIssuer.BearerFor(newId, username, "KD", "SALES")).GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(newId, (await ReadJsonAsync(response)).GetProperty("id").GetGuid());
        Assert.False(await ScalarAsync<bool>("SELECT active FROM identity.app_user WHERE id = @id", new { id = oldId }));
        Assert.Equal(1, await EventsForAsync("UserDeactivated", oldId));
    }

    [Fact]
    public async Task ConcurrentFirstRequestsOfNewUserDoNotFail()
    {
        var id = Guid.NewGuid();
        var bearer = TestJwtIssuer.BearerFor(id, "race-" + id.ToString()[..8], "KD", "SALES");
        var statuses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(async _ => (int)(await ClientWith(bearer).GetAsync("/api/v1/me")).StatusCode));

        Assert.All(statuses, status => Assert.Equal(200, status));
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM identity.app_user WHERE id = @id", new { id }));
    }

    private static List<string> Strings(System.Text.Json.JsonElement json, string property) =>
        json.GetProperty(property).EnumerateArray().Select(e => e.GetString()!).ToList();
}
