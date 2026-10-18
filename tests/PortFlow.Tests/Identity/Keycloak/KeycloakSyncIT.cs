using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PortFlow.Identity.Api;
using PortFlow.Tests.Support.Security;

namespace PortFlow.Tests.Identity.Keycloak;

public class KeycloakSyncIT(KeycloakFixture fixture) : KeycloakIntegrationTest(fixture)
{
    [Fact]
    public async Task ManualSyncMirrorsSeedUsersWithRolesAndDepartment()
    {
        await SyncNowAsync();
        Assert.Equal("MANUAL", await ScalarAsync<string>("SELECT sync_source FROM identity.app_user WHERE id = @id",
            new { id = SeedUser.OPS01.Id }));
        Assert.Equal(["OPERATIONS", "OPERATIONS_LEAD"], await ListAsync<string>(
            "SELECT role_code FROM identity.user_role WHERE user_id = @id ORDER BY role_code", new { id = SeedUser.OPS01.Id }));
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM identity.app_user WHERE username LIKE 'service-account-%'"));
    }

    [Fact]
    public async Task NewKeycloakUserIsAssignableBeforeFirstLogin()
    {
        var id = await Kc.CreateUserAsync("legal-" + Guid.NewGuid().ToString()[..8], "PC", "LEGAL");
        await SyncNowAsync();

        Assert.Contains(id, (await Directory(d => d.FindActiveByRoleAsync("LEGAL"))).Select(u => u.Id));
        Assert.Equal(SeedUser.DEPT_PC, (await Directory(d => d.FindAsync(id)))?.DepartmentId);
        Assert.True(await ScalarAsync<bool>("SELECT last_seen_at IS NULL FROM identity.app_user WHERE id = @id", new { id }));
    }

    [Fact]
    public async Task DisabledUserIsBlockedRightAfterManualSync()
    {
        var oldToken = ClientWith("Bearer " + await Kc.AccessTokenAsync("acc01"));
        Assert.Equal(HttpStatusCode.OK, (await oldToken.GetAsync("/api/v1/me")).StatusCode);
        try
        {
            await Kc.SetEnabledAsync(SeedUser.ACC01.Id, false);
            await SyncNowAsync();
            var response = await oldToken.GetAsync("/api/v1/me");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("AUTH_USER_INACTIVE", await ErrorCodeAsync(response));
            Assert.True(await EventsForAsync("UserDeactivated", SeedUser.ACC01.Id) > 0);
        }
        finally
        {
            await Kc.SetEnabledAsync(SeedUser.ACC01.Id, true);
            await SyncNowAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await oldToken.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task UserDeletedInKeycloakBecomesInactive()
    {
        var id = await Kc.CreateUserAsync("gone-" + Guid.NewGuid().ToString()[..8], "KD", "SALES");
        await SyncNowAsync();
        Assert.True(await Directory(d => d.IsActiveAsync(id)));

        await Kc.DeleteUserAsync(id);
        await SyncNowAsync();
        Assert.False(await Directory(d => d.IsActiveAsync(id)));
    }

    [Fact]
    public async Task AdminGrantedApproverRoleIsFlaggedOnSync()
    {
        var id = await Kc.CreateUserAsync("adm-" + Guid.NewGuid().ToString()[..8], "IT", "ADMIN");
        await SyncNowAsync();
        await Kc.AddRealmRolesAsync(id, "DIRECTOR");
        await SyncNowAsync();

        Assert.True(await ScalarAsync<bool>("SELECT sod_violation FROM identity.app_user WHERE id = @id", new { id }));
        Assert.True(await EventsForAsync("SodViolationDetected", id) > 0);
        Assert.True(await EventsForAsync("UserRolesChanged", id) > 0);
    }

    private async Task SyncNowAsync()
    {
        var admin = ClientWith("Bearer " + await Kc.AccessTokenAsync("admin"));
        var response = await admin.PostAsync("/api/v1/admin/users/sync", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<T> Directory<T>(Func<IUserDirectory, Task<T>> query)
    {
        await using var scope = Fixture.App.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<IUserDirectory>());
    }
}
