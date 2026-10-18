using Dapper;
using PortFlow.Identity.Api;
using PortFlow.Shared.Persistence;

namespace PortFlow.Identity.Infrastructure.Persistence;

/// <summary><see cref="IUserDirectory"/> trên bản sao (= UserDirectoryJdbc): user seed/đồng bộ chưa đăng nhập vẫn tra được.</summary>
internal sealed class UserDirectory(DbSession session) : IUserDirectory
{
    private const string UserColumns =
        "u.id AS Id, u.username AS Username, u.full_name AS FullName, u.department_id AS DepartmentId, u.email AS Email";

    public async Task<UserRef?> FindAsync(Guid userId, CancellationToken ct = default) =>
        await QuerySingleAsync<UserRef>($"SELECT {UserColumns} FROM identity.app_user u WHERE u.id = @userId",
            new { userId }, ct);

    public Task<IReadOnlyList<UserRef>> FindActiveByRoleAsync(string roleCode, CancellationToken ct = default) =>
        QueryAsync<UserRef>($"""
            SELECT {UserColumns} FROM identity.app_user u JOIN identity.user_role ur ON ur.user_id = u.id
            WHERE u.active AND u.department_id IS NOT NULL AND ur.role_code = @roleCode
            ORDER BY u.full_name, u.id
            """, new { roleCode }, ct);

    public Task<IReadOnlyList<UserRef>> FindActiveByRoleInDepartmentAsync(string roleCode, Guid departmentId,
        CancellationToken ct = default) =>
        QueryAsync<UserRef>($"""
            SELECT {UserColumns} FROM identity.app_user u JOIN identity.user_role ur ON ur.user_id = u.id
            WHERE u.active AND u.department_id = @departmentId AND ur.role_code = @roleCode
            ORDER BY u.full_name, u.id
            """, new { roleCode, departmentId }, ct);

    public Task<UserRef?> HeadOfDepartmentAsync(Guid departmentId, CancellationToken ct = default) =>
        QuerySingleAsync<UserRef>($"""
            SELECT {UserColumns} FROM identity.department d JOIN identity.app_user u ON u.id = d.head_user_id
            WHERE d.id = @departmentId AND u.active
            """, new { departmentId }, ct);

    public Task<UserRef?> HeadOfDepartmentOfAsync(Guid userId, CancellationToken ct = default) =>
        QuerySingleAsync<UserRef>($"""
            SELECT {UserColumns} FROM identity.app_user owner
            JOIN identity.department d ON d.id = owner.department_id
            JOIN identity.app_user u ON u.id = d.head_user_id
            WHERE owner.id = @userId AND u.active
            """, new { userId }, ct);

    public async Task<bool> IsActiveAsync(Guid userId, CancellationToken ct = default)
    {
        var connection = await session.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<bool?>(new CommandDefinition(
            "SELECT active FROM identity.app_user WHERE id = @userId", new { userId }, session.Transaction,
            cancellationToken: ct)) ?? false;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> FullNamesAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var distinct = ids.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }
        var connection = await session.OpenAsync(ct);
        var rows = await connection.QueryAsync<(Guid Id, string FullName)>(new CommandDefinition(
            "SELECT id, full_name FROM identity.app_user WHERE id = ANY(@ids)", new { ids = distinct.ToArray() }, session.Transaction,
            cancellationToken: ct));
        return rows.ToDictionary(r => r.Id, r => r.FullName);
    }

    public Task<string?> DepartmentCodeOfAsync(Guid userId, CancellationToken ct = default) =>
        QuerySingleAsync<string>("""
            SELECT d.code FROM identity.app_user u JOIN identity.department d ON d.id = u.department_id
            WHERE u.id = @userId
            """, new { userId }, ct);

    public async Task<IReadOnlySet<string>?> PermissionsOfRoleAsync(string roleCode, CancellationToken ct = default)
    {
        var permissions = await QuerySingleAsync<string[]>(
            "SELECT permissions FROM identity.role WHERE code = @roleCode", new { roleCode }, ct);
        return permissions?.ToHashSet();
    }

    private async Task<T?> QuerySingleAsync<T>(string sql, object args, CancellationToken ct)
    {
        var connection = await session.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<T>(new CommandDefinition(sql, args, session.Transaction,
            cancellationToken: ct));
    }

    private async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object args, CancellationToken ct)
    {
        var connection = await session.OpenAsync(ct);
        var rows = await connection.QueryAsync<T>(new CommandDefinition(sql, args, session.Transaction,
            cancellationToken: ct));
        return rows.ToList();
    }
}
