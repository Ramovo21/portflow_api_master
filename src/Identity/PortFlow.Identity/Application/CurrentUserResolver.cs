using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using PortFlow.Identity.Api;
using PortFlow.Identity.Domain;
using PortFlow.Identity.Infrastructure.Security;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Persistence;
using PortFlow.Shared.Time;

namespace PortFlow.Identity.Application;

/// <summary>Các claim PortFlow dùng từ access token Keycloak.</summary>
public sealed record TokenClaims(string? Subject, string? PreferredUsername, string? Name, string? Email,
    string? Department);

/// <summary>Trạng thái bản sao cần cho mỗi request (cache theo user + "dấu vân tay" claim).</summary>
public sealed record MirrorState(Guid Id, string Username, string FullName, Guid? DepartmentId, bool Active,
    bool SodViolation);

public interface IUserMirrorSync
{
    Task<MirrorState> SyncFromTokenAsync(KeycloakUserSnapshot claims, CancellationToken ct);
}

/// <summary>Cache bản sao 60s, tối đa 10.000 user; claim trong token đổi thì đọc lại DB.</summary>
public sealed class CurrentUserCache : IDisposable
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 10_000 });

    internal async Task<MirrorState> GetAsync(Guid userId, string claimsFingerprint, Func<Task<MirrorState>> loader)
    {
        if (_cache.TryGetValue(userId, out Entry? entry) && entry is not null && entry.Fingerprint == claimsFingerprint)
        {
            return entry.State;
        }
        var state = await loader();
        _cache.Set(userId, new Entry(claimsFingerprint, state),
            new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = Ttl });
        return state;
    }

    public void Evict(Guid userId) => _cache.Remove(userId);

    public void EvictAll() => _cache.Clear();

    public void Dispose() => _cache.Dispose();

    private sealed record Entry(string Fingerprint, MirrorState State);
}

/// <summary>
/// JWT → <see cref="TokenCurrentUser"/>: đồng bộ JIT bản sao, chặn user bị khóa/chưa có phòng ban,
/// tính permission từ role trong DB và loại <c>Workflow.Act</c> nếu vi phạm SoD.
/// </summary>
public sealed class CurrentUserResolver(
    IUserMirrorSync sync,
    CurrentUserCache cache,
    IRolePermissions roles,
    IBusinessClock clock)
{
    public async Task<TokenCurrentUser> ResolveAsync(TokenClaims token, IReadOnlySet<string> tokenRoles,
        CancellationToken ct = default)
    {
        var claims = Claims(token, tokenRoles);
        var mirror = await cache.GetAsync(claims.Id, Fingerprint(claims), () => SyncWithRetryAsync(claims, ct));
        if (!mirror.Active)
        {
            throw new DomainException(AuthErrorCode.AUTH_USER_INACTIVE);
        }
        if (mirror.DepartmentId is null)
        {
            throw new DomainException(AuthErrorCode.AUTH_USER_NOT_PROVISIONED,
                "The account has no valid department assigned in Keycloak (department attribute)");
        }
        var knownRoles = await roles.KnownRolesAsync(tokenRoles, ct);
        var permissions = await roles.PermissionsOfAsync(knownRoles, ct);
        var sod = mirror.SodViolation || RoleAssignmentPolicy.ViolatesSoD(knownRoles, permissions);
        if (sod)
        {
            permissions = permissions.Where(p => p != Permission.WORKFLOW_ACT).ToHashSet();
        }
        return new TokenCurrentUser(mirror.Id, mirror.Username, mirror.FullName, mirror.DepartmentId, knownRoles,
            permissions, await roles.DataScopeOfAsync(knownRoles, ct), sod);
    }

    /// <summary>Hai request đầu tiên của user mới chạy song song → một bên trùng khóa → đọc lại một lần.</summary>
    private async Task<MirrorState> SyncWithRetryAsync(KeycloakUserSnapshot claims, CancellationToken ct)
    {
        try
        {
            return await sync.SyncFromTokenAsync(claims, ct);
        }
        catch (Exception ex) when (IsConcurrentUpdate(ex))
        {
            return await sync.SyncFromTokenAsync(claims, ct);
        }
    }

    private static bool IsConcurrentUpdate(Exception ex) =>
        ex is DbUpdateException or OptimisticLockingFailureException
        || (ex is PostgresException pg && pg.SqlState.StartsWith("23", StringComparison.Ordinal));

    private KeycloakUserSnapshot Claims(TokenClaims token, IReadOnlySet<string> tokenRoles)
    {
        if (!Guid.TryParse(token.Subject, out var id))
        {
            throw new DomainException(AuthErrorCode.AUTH_INVALID_TOKEN, "Claim sub is not a UUID");
        }
        var username = token.PreferredUsername ?? id.ToString();
        var fullName = string.IsNullOrWhiteSpace(token.Name) ? username : token.Name;
        return new KeycloakUserSnapshot(id, username, fullName, token.Email, token.Department, true, tokenRoles,
            clock.Now(), KeycloakUserSnapshot.JIT);
    }

    private static string Fingerprint(KeycloakUserSnapshot claims) =>
        string.Join("|", claims.Username, claims.FullName, claims.Email ?? "null", claims.DepartmentCode ?? "null",
            string.Join(",", claims.Roles.Order(StringComparer.Ordinal)));
}
