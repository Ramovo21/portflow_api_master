using Microsoft.EntityFrameworkCore;
using NodaTime;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PortFlow.Identity.Application;
using PortFlow.Identity.Domain;
using PortFlow.Identity.Infrastructure.Security;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Time;

namespace PortFlow.Tests.Identity.Application;

public class CurrentUserResolverTests
{
    private static readonly Guid Id = Guid.Parse("0192f0a1-0000-7000-8000-000000000009");
    private static readonly Guid Dept = Guid.NewGuid();

    private readonly IUserMirrorSync _sync = Substitute.For<IUserMirrorSync>();
    private readonly IRolePermissions _roles = Substitute.For<IRolePermissions>();
    private readonly CurrentUserResolver _resolver;

    public CurrentUserResolverTests()
    {
        _resolver = new CurrentUserResolver(_sync, new CurrentUserCache(), _roles, new FixedClock());
    }

    [Fact]
    public async Task PermissionsComeFromDatabaseAndUnknownRolesAreDropped()
    {
        Mirror(true, Dept, false);
        Roles(["LEGAL"], ["Contract.Read", "Workflow.Act"], DataScope.ALL);

        var user = await _resolver.ResolveAsync(Token(), Set("LEGAL", "default-roles-portflow"));

        Assert.Equal(["LEGAL"], user.Roles);
        Assert.Equal(Set("Contract.Read", "Workflow.Act"), user.Permissions);
        Assert.Equal(DataScope.ALL, user.DataScope);
    }

    [Fact]
    public async Task SodViolationRemovesWorkflowAct()
    {
        Mirror(true, Dept, false);
        Roles(["ADMIN", "DIRECTOR"], ["User.Admin", "Workflow.Act"], DataScope.ALL);

        var user = await _resolver.ResolveAsync(Token(), Set("ADMIN", "DIRECTOR"));

        Assert.True(user.SodViolation);
        Assert.Equal(["User.Admin"], user.Permissions);
    }

    [Fact]
    public async Task FlaggedMirrorAlsoRemovesWorkflowAct()
    {
        Mirror(true, Dept, true);
        Roles(["DIRECTOR"], ["Workflow.Act", "Audit.ReadAll"], DataScope.ALL);

        Assert.Equal(["Audit.ReadAll"], (await _resolver.ResolveAsync(Token(), Set("DIRECTOR"))).Permissions);
    }

    [Fact]
    public async Task InactiveUserIsRejected()
    {
        Mirror(false, Dept, false);
        var ex = await Assert.ThrowsAsync<DomainException>(() => _resolver.ResolveAsync(Token(), Set("SALES")));
        Assert.Equal(AuthErrorCode.AUTH_USER_INACTIVE, ex.Code);
    }

    [Fact]
    public async Task UserWithoutDepartmentIsNotProvisioned()
    {
        Mirror(true, null, false);
        var ex = await Assert.ThrowsAsync<DomainException>(() => _resolver.ResolveAsync(Token(), Set("SALES")));
        Assert.Equal(AuthErrorCode.AUTH_USER_NOT_PROVISIONED, ex.Code);
    }

    [Fact]
    public async Task NonUuidSubjectIsInvalidToken()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            _resolver.ResolveAsync(Token() with { Subject = "not-a-uuid" }, Set()));
        Assert.Equal(AuthErrorCode.AUTH_INVALID_TOKEN, ex.Code);
    }

    [Fact]
    public async Task SameClaimsHitCacheWithoutTouchingDatabase()
    {
        Mirror(true, Dept, false);
        Roles([], [], DataScope.DEPARTMENT);

        await _resolver.ResolveAsync(Token(), Set("SALES"));
        await _resolver.ResolveAsync(Token(), Set("SALES"));
        await _resolver.ResolveAsync(Token(), Set("SALES", "LEGAL"));

        await _sync.Received(2).SyncFromTokenAsync(Arg.Any<KeycloakUserSnapshot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConcurrentFirstInsertIsRetriedOnce()
    {
        Roles([], [], DataScope.DEPARTMENT);
        _sync.SyncFromTokenAsync(Arg.Any<KeycloakUserSnapshot>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new DbUpdateException("duplicate key"),
                _ => Task.FromResult(new MirrorState(Id, "admin", "Quản Trị", Dept, true, false)));

        Assert.Equal(Id, (await _resolver.ResolveAsync(Token(), Set())).Id);
    }

    private void Mirror(bool active, Guid? departmentId, bool sod) =>
        _sync.SyncFromTokenAsync(Arg.Any<KeycloakUserSnapshot>(), Arg.Any<CancellationToken>())
            .Returns(new MirrorState(Id, "admin", "Quản Trị", departmentId, active, sod));

    private void Roles(string[] known, string[] permissions, DataScope scope)
    {
        _roles.KnownRolesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns(Set(known));
        _roles.PermissionsOfAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns(Set(permissions));
        _roles.DataScopeOfAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns(scope);
    }

    private static IReadOnlySet<string> Set(params string[] values) => values.ToHashSet();

    private static TokenClaims Token() => new(Id.ToString(), "admin", "Quản Trị", null, "IT");

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeZone Zone => IBusinessClock.DefaultZone;

        public Instant Now() => Instant.FromUtc(2026, 10, 20, 3, 0);
    }
}
