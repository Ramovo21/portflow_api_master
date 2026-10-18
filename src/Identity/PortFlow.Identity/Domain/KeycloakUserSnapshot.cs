using NodaTime;

namespace PortFlow.Identity.Domain;

public enum DataScope
{
    DEPARTMENT,
    ALL,
}

/// <summary>Trạng thái một user theo Keycloak (từ token khi JIT, hoặc từ Admin API khi đồng bộ).</summary>
public sealed record KeycloakUserSnapshot
{
    public const string JIT = "JIT";
    public const string JOB = "JOB";
    public const string MANUAL = "MANUAL";

    public KeycloakUserSnapshot(Guid id, string username, string fullName, string? email, string? departmentCode,
        bool enabled, IEnumerable<string> roles, Instant syncedAt, string syncSource)
    {
        Id = id;
        Username = username;
        FullName = fullName;
        Email = email;
        DepartmentCode = departmentCode;
        Enabled = enabled;
        Roles = roles.ToHashSet();
        SyncedAt = syncedAt;
        SyncSource = syncSource;
    }

    public Guid Id { get; }

    public string Username { get; }

    public string FullName { get; }

    public string? Email { get; }

    public string? DepartmentCode { get; }

    public bool Enabled { get; }

    public IReadOnlySet<string> Roles { get; }

    public Instant SyncedAt { get; }

    public string SyncSource { get; }
}

public static class RoleAssignmentPolicy
{
    public const string ADMIN_ROLE = "ADMIN";

    /// <summary>SoD: Quản trị không được đồng thời có quyền duyệt (Workflow.Act).</summary>
    public static bool ViolatesSoD(IReadOnlySet<string> roleCodes, IReadOnlySet<string> rolePermissions) =>
        roleCodes.Contains(ADMIN_ROLE) && rolePermissions.Contains(Api.Permission.WORKFLOW_ACT);
}
