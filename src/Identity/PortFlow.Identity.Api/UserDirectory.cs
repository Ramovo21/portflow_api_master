namespace PortFlow.Identity.Api;

public sealed record UserRef(Guid Id, string Username, string FullName, Guid? DepartmentId, string? Email);

/// <summary>Tra cứu người dùng trên bản sao Keycloak (assignee workflow, tên hiển thị...).</summary>
public interface IUserDirectory
{
    Task<UserRef?> FindAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<UserRef>> FindActiveByRoleAsync(string roleCode, CancellationToken ct = default);

    Task<IReadOnlyList<UserRef>> FindActiveByRoleInDepartmentAsync(string roleCode, Guid departmentId,
        CancellationToken ct = default);

    Task<UserRef?> HeadOfDepartmentAsync(Guid departmentId, CancellationToken ct = default);

    Task<UserRef?> HeadOfDepartmentOfAsync(Guid userId, CancellationToken ct = default);

    Task<bool> IsActiveAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, string>> FullNamesAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

    Task<string?> DepartmentCodeOfAsync(Guid userId, CancellationToken ct = default);

    /// <summary>null = role không tồn tại.</summary>
    Task<IReadOnlySet<string>?> PermissionsOfRoleAsync(string roleCode, CancellationToken ct = default);
}
