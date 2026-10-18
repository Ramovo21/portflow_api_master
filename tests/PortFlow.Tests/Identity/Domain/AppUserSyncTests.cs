using NodaTime;
using PortFlow.Identity.Api.Events;
using PortFlow.Identity.Domain;
using PortFlow.Shared.Events;

namespace PortFlow.Tests.Identity.Domain;

public class AppUserSyncTests
{
    private static readonly IReadOnlySet<string> Known = new HashSet<string> { "SALES", "LEGAL", "DIRECTOR", "ADMIN" };
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly Guid Dept = Guid.NewGuid();

    [Fact]
    public void FirstSyncProvisionsUserWithKnownRolesOnly()
    {
        var user = AppUser.FirstSeen(Id);
        var events = user.ApplySync(Snapshot(true, "SALES", "default-roles-portflow"), Dept, Known, false, Meta);

        var provisioned = Assert.IsType<UserProvisioned>(Assert.Single(events));
        Assert.Equal(Id, provisioned.UserId);
        Assert.Equal(Dept, provisioned.DepartmentId);
        Assert.Equal(["SALES"], provisioned.Roles);
        Assert.Equal(["SALES"], user.RoleCodes);
        Assert.True(user.Active);
        Assert.True(user.IsProvisioned);
        Assert.False(user.IsFresh);
    }

    [Fact]
    public void FirstSyncWithoutDepartmentIsUnprovisioned()
    {
        var user = AppUser.FirstSeen(Id);
        user.ApplySync(Snapshot(true, "LEGAL"), null, Known, false, Meta);
        Assert.False(user.IsProvisioned);
    }

    [Fact]
    public void RoleChangePublishesUserRolesChanged()
    {
        var user = Synced("SALES");
        var changed = Assert.IsType<UserRolesChanged>(Assert.Single(
            user.ApplySync(Snapshot(true, "LEGAL"), Dept, Known, false, Meta)));
        Assert.Equal(["SALES"], changed.OldRoles);
        Assert.Equal(["LEGAL"], changed.NewRoles);
    }

    [Fact]
    public void UnchangedSyncPublishesNothing()
    {
        var user = Synced("SALES");
        Assert.Empty(user.ApplySync(Snapshot(true, "SALES"), Dept, Known, false, Meta));
    }

    [Fact]
    public void DisablingPublishesUserDeactivatedOnce()
    {
        var user = Synced("SALES");
        Assert.IsType<UserDeactivated>(Assert.Single(user.ApplySync(Snapshot(false, "SALES"), Dept, Known, false, Meta)));
        Assert.False(user.Active);
        Assert.Empty(user.ApplySync(Snapshot(false, "SALES"), Dept, Known, false, Meta));
    }

    [Fact]
    public void SodViolationIsFlaggedAndPublishedOnlyOnTransition()
    {
        var user = Synced("ADMIN");
        var events = user.ApplySync(Snapshot(true, "ADMIN", "DIRECTOR"), Dept, Known, true, Meta);
        Assert.Contains(events, e => e is SodViolationDetected);
        Assert.True(user.SodViolation);
        Assert.Empty(user.ApplySync(Snapshot(true, "ADMIN", "DIRECTOR"), Dept, Known, true, Meta));
    }

    [Fact]
    public void DeactivateWhenMissingFromKeycloak()
    {
        var user = Synced("SALES");
        Assert.IsType<UserDeactivated>(Assert.Single(user.Deactivate(Now(), "JOB", Meta)));
        Assert.Empty(user.Deactivate(Now(), "JOB", Meta));
    }

    [Fact]
    public void MatchesTokenIgnoresUnknownRoles()
    {
        var user = Synced("SALES");
        Assert.True(user.MatchesToken(Snapshot(true, "SALES", "offline_access"), Dept, Known));
        Assert.False(user.MatchesToken(Snapshot(true, "LEGAL"), Dept, Known));
        Assert.False(user.MatchesToken(Snapshot(true, "SALES"), Guid.NewGuid(), Known));
    }

    private static AppUser Synced(params string[] roles)
    {
        var user = AppUser.FirstSeen(Id);
        user.ApplySync(Snapshot(true, roles), Dept, Known, false, Meta);
        return user;
    }

    private static KeycloakUserSnapshot Snapshot(bool enabled, params string[] roles) =>
        new(Id, "sales01", "Nguyễn Văn An", "an@abc.vn", "KD", enabled, roles, Now(), KeycloakUserSnapshot.JOB);

    private static Instant Now() => SystemClock.Instance.GetCurrentInstant();

    private static EventMetadata Meta() => new(Guid.NewGuid(), Now(), Guid.Empty, "test", "cid");
}
