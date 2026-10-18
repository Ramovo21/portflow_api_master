using NodaTime;
using PortFlow.Identity.Api.Events;
using PortFlow.Shared.Events;

namespace PortFlow.Identity.Domain;

/// <summary>Bản sao người dùng Keycloak (<c>identity.app_user</c>); id = claim <c>sub</c>.</summary>
public sealed class AppUser
{
    private readonly List<UserRole> _roles = [];
    private bool _fresh;

    private AppUser()
    {
    }

    public Guid Id { get; private set; }

    public string Username { get; private set; } = null!;

    public string FullName { get; private set; } = null!;

    public string? Email { get; private set; }

    public Guid? DepartmentId { get; private set; }

    public bool Active { get; private set; }

    public bool SodViolation { get; private set; }

    public Instant SyncedAt { get; private set; }

    public string SyncSource { get; private set; } = KeycloakUserSnapshot.JIT;

    public Instant? LastSeenAt { get; private set; }

    public long Version { get; private set; }

    public IReadOnlySet<string> RoleCodes => _roles.Select(r => r.RoleCode).ToHashSet();

    public bool IsFresh => _fresh;

    /// <summary>Đã gán phòng ban hợp lệ (ngược lại = Unprovisioned).</summary>
    public bool IsProvisioned => DepartmentId is not null;

    public static AppUser FirstSeen(Guid id) => new() { Id = id, _fresh = true };

    public IReadOnlyList<IDomainEvent> ApplySync(KeycloakUserSnapshot snapshot, Guid? departmentId,
        IReadOnlySet<string> knownRoleCodes, bool violatesSoD, Func<EventMetadata> metadata)
    {
        if (Id != snapshot.Id)
        {
            throw new ArgumentException($"Snapshot belongs to a different user: {snapshot.Id}");
        }
        var oldRoles = RoleCodes;
        var newRoles = snapshot.Roles.Where(knownRoleCodes.Contains).ToHashSet();
        var wasActive = Active;
        var wasSodViolation = SodViolation;

        Username = snapshot.Username;
        FullName = snapshot.FullName;
        Email = snapshot.Email;
        DepartmentId = departmentId;
        Active = snapshot.Enabled;
        SodViolation = violatesSoD;
        // Chỉ thêm/bớt phần khác biệt để EF không xóa rồi chèn lại cùng khóa (user_id, role_code).
        _roles.RemoveAll(r => !newRoles.Contains(r.RoleCode));
        foreach (var role in newRoles.Where(r => !oldRoles.Contains(r)))
        {
            _roles.Add(new UserRole(role));
        }
        SyncedAt = snapshot.SyncedAt;
        SyncSource = snapshot.SyncSource;

        var events = new List<IDomainEvent>();
        if (_fresh)
        {
            events.Add(new UserProvisioned(metadata(), Id, Username, DepartmentId, newRoles));
        }
        else
        {
            if (!oldRoles.SetEquals(newRoles))
            {
                events.Add(new UserRolesChanged(metadata(), Id, oldRoles, newRoles));
            }
            if (wasActive && !Active)
            {
                events.Add(new UserDeactivated(metadata(), Id, Username));
            }
        }
        if (SodViolation && (_fresh || !wasSodViolation))
        {
            events.Add(new SodViolationDetected(metadata(), Id, newRoles));
        }
        _fresh = false;
        return events;
    }

    public IReadOnlyList<IDomainEvent> Deactivate(Instant now, string source, Func<EventMetadata> metadata)
    {
        if (!Active)
        {
            return [];
        }
        Active = false;
        SyncedAt = now;
        SyncSource = source;
        return [new UserDeactivated(metadata(), Id, Username)];
    }

    /// <summary>Tài khoản Keycloak bị tạo lại cùng username: bản ghi cũ nhường username và bị vô hiệu hóa.</summary>
    public IReadOnlyList<IDomainEvent> ReleaseUsername(Instant now, string source, Func<EventMetadata> metadata)
    {
        var events = Deactivate(now, source, metadata);
        Username = Username + "#" + Id;
        return events;
    }

    public bool MatchesToken(KeycloakUserSnapshot claims, Guid? departmentId, IReadOnlySet<string> knownRoleCodes)
    {
        var tokenRoles = claims.Roles.Where(knownRoleCodes.Contains).ToHashSet();
        return !_fresh && Username == claims.Username && FullName == claims.FullName && Email == claims.Email
               && DepartmentId == departmentId && RoleCodes.SetEquals(tokenRoles);
    }
}

/// <summary>Dòng <c>identity.user_role</c> (owned bởi <see cref="AppUser"/>).</summary>
public sealed class UserRole(string roleCode)
{
    public string RoleCode { get; private set; } = roleCode;
}
