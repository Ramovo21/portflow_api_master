using Dapper;
using NodaTime;
using PortFlow.Shared.Persistence;

namespace PortFlow.Identity.Application;

public sealed record UserRow
{
    public Guid Id { get; init; }

    public string Username { get; init; } = null!;

    public string FullName { get; init; } = null!;

    public string? Email { get; init; }

    public Guid? DepartmentId { get; init; }

    public string? DepartmentCode { get; init; }

    public string? DepartmentName { get; init; }

    public bool Active { get; init; }

    public bool SodViolation { get; init; }

    public Instant? SyncedAt { get; init; }

    public string? SyncSource { get; init; }

    public Instant? LastSeenAt { get; init; }

    public string[] Roles { get; init; } = [];
}

public sealed record UserOption(Guid Id, string FullName, string Username, string? DepartmentName);

public sealed record DepartmentRef(Guid Id, string Code, string Name);

/// <summary>Truy vấn đọc (CQRS nhẹ) cho màn hình quản trị người dùng.</summary>
public sealed class IdentityQueries(DbSession session)
{
    public async Task<IReadOnlyList<UserRow>> UsersAsync(string? query, Guid? departmentId, string? role, bool? active,
        CancellationToken ct)
    {
        var connection = await session.OpenAsync(ct);
        var rows = await connection.QueryAsync<UserRow>(new CommandDefinition("""
            SELECT u.id AS Id, u.username AS Username, u.full_name AS FullName, u.email AS Email,
                   u.department_id AS DepartmentId, d.code AS DepartmentCode, d.name AS DepartmentName,
                   u.active AS Active, u.sod_violation AS SodViolation, u.synced_at AS SyncedAt,
                   u.sync_source AS SyncSource, u.last_seen_at AS LastSeenAt,
                   ARRAY(SELECT ur.role_code FROM identity.user_role ur WHERE ur.user_id = u.id ORDER BY ur.role_code)
                       AS Roles
            FROM identity.app_user u
            LEFT JOIN identity.department d ON d.id = u.department_id
            WHERE (CAST(@query AS text) IS NULL
                   OR u.username ILIKE '%' || CAST(@query AS text) || '%'
                   OR u.full_name ILIKE '%' || CAST(@query AS text) || '%')
              AND (CAST(@departmentId AS uuid) IS NULL OR u.department_id = CAST(@departmentId AS uuid))
              AND (CAST(@role AS text) IS NULL OR EXISTS (
                   SELECT 1 FROM identity.user_role x WHERE x.user_id = u.id AND x.role_code = CAST(@role AS text)))
              AND (CAST(@active AS boolean) IS NULL OR u.active = CAST(@active AS boolean))
            ORDER BY u.username
            """, new { query = BlankToNull(query), departmentId, role = BlankToNull(role), active }, session.Transaction,
            cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<bool> HoldsPermissionAsync(Guid userId, string permission, CancellationToken ct)
    {
        var connection = await session.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (SELECT 1 FROM identity.user_role ur JOIN identity.role r ON r.code = ur.role_code
                           WHERE ur.user_id = @userId AND CAST(@permission AS text) = ANY (r.permissions))
            """, new { userId, permission }, session.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<UserOption>> ActiveUsersHoldingAsync(string permission, string? query, int limit,
        CancellationToken ct)
    {
        var connection = await session.OpenAsync(ct);
        var rows = await connection.QueryAsync<UserOption>(new CommandDefinition("""
            SELECT u.id AS Id, u.full_name AS FullName, u.username AS Username, d.name AS DepartmentName
            FROM identity.app_user u
            LEFT JOIN identity.department d ON d.id = u.department_id
            WHERE u.active
              AND EXISTS (SELECT 1 FROM identity.user_role ur JOIN identity.role r ON r.code = ur.role_code
                          WHERE ur.user_id = u.id AND CAST(@permission AS text) = ANY (r.permissions))
              AND (CAST(@query AS text) IS NULL
                   OR u.username ILIKE '%' || CAST(@query AS text) || '%'
                   OR u.full_name ILIKE '%' || CAST(@query AS text) || '%')
            ORDER BY u.full_name
            LIMIT @limit
            """, new { permission, query = BlankToNull(query), limit }, session.Transaction, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<DepartmentRef?> DepartmentAsync(Guid? departmentId, CancellationToken ct)
    {
        if (departmentId is null)
        {
            return null;
        }
        var connection = await session.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<DepartmentRef>(new CommandDefinition(
            "SELECT id AS Id, code AS Code, name AS Name FROM identity.department WHERE id = @departmentId",
            new { departmentId }, session.Transaction, cancellationToken: ct));
    }

    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
