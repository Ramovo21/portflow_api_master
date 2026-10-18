using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using PortFlow.Identity.Domain;

namespace PortFlow.Identity.Infrastructure.Security;

/// <summary>Role → permission/scope lấy từ DB (nguồn sự thật của AuthZ), cache 60s; sửa role thì xóa cache.</summary>
public interface IRolePermissions
{
    Task<IReadOnlySet<string>> KnownRolesAsync(IEnumerable<string> roleCodes, CancellationToken ct = default);

    Task<IReadOnlySet<string>> PermissionsOfAsync(IEnumerable<string> roleCodes, CancellationToken ct = default);

    Task<DataScope> DataScopeOfAsync(IEnumerable<string> roleCodes, CancellationToken ct = default);

    void EvictAll();
}

internal sealed class RolePermissionCache(NpgsqlDataSource dataSource) : IRolePermissions, IDisposable
{
    private const string AllRoles = "all";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public async Task<IReadOnlySet<string>> KnownRolesAsync(IEnumerable<string> roleCodes, CancellationToken ct = default)
    {
        var all = await AllAsync(ct);
        return roleCodes.Where(all.ContainsKey).ToHashSet();
    }

    public async Task<IReadOnlySet<string>> PermissionsOfAsync(IEnumerable<string> roleCodes, CancellationToken ct = default)
    {
        var all = await AllAsync(ct);
        return roleCodes.Select(all.GetValueOrDefault).OfType<RoleEntry>()
            .SelectMany(role => role.Permissions)
            .ToHashSet();
    }

    public async Task<DataScope> DataScopeOfAsync(IEnumerable<string> roleCodes, CancellationToken ct = default)
    {
        var all = await AllAsync(ct);
        return roleCodes.Select(all.GetValueOrDefault).OfType<RoleEntry>().Any(role => role.DataScope == DataScope.ALL)
            ? DataScope.ALL
            : DataScope.DEPARTMENT;
    }

    public void EvictAll() => _cache.Clear();

    private async Task<IReadOnlyDictionary<string, RoleEntry>> AllAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(AllRoles, out IReadOnlyDictionary<string, RoleEntry>? cached) && cached is not null)
        {
            return cached;
        }
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<(string Code, string[] Permissions, string DataScope)>(
            new CommandDefinition("SELECT code, permissions, data_scope FROM identity.role", cancellationToken: ct));
        var roles = rows.ToDictionary(r => r.Code,
            r => new RoleEntry(r.Permissions, Enum.Parse<DataScope>(r.DataScope)));
        _cache.Set(AllRoles, (IReadOnlyDictionary<string, RoleEntry>)roles, Ttl);
        return roles;
    }

    public void Dispose() => _cache.Dispose();

    private sealed record RoleEntry(string[] Permissions, DataScope DataScope);
}
