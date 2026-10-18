using Microsoft.Extensions.Logging;
using PortFlow.Identity.Api;
using PortFlow.Identity.Domain;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Security;

namespace PortFlow.Identity.Infrastructure.Security;

/// <summary>
/// Tầng 2 – scope: user scope DEPARTMENT chỉ thao tác hồ sơ của phòng mình; scope ALL thì mọi phòng.
/// Bản ghi không tồn tại → 404 (không lộ sự tồn tại qua 403).
/// </summary>
public sealed class ScopedPermissionEvaluator : IScopePermissionEvaluator
{
    private readonly IReadOnlyDictionary<string, IScopeResolver> _resolvers;
    private readonly IRolePermissions _roles;
    private readonly ILogger<ScopedPermissionEvaluator> _log;

    public ScopedPermissionEvaluator(IEnumerable<IScopeResolver> resolvers, IRolePermissions roles,
        ILogger<ScopedPermissionEvaluator> log)
    {
        _resolvers = resolvers.ToDictionary(r => r.ResourceType);
        _roles = roles;
        _log = log;
    }

    public async Task<bool> HasPermissionAsync(ICurrentUser? user, object? targetId, string resourceType,
        CancellationToken ct = default)
    {
        if (user is null || user.IsSystem)
        {
            return false;
        }
        var id = ToGuid(targetId);
        if (id is null || !_resolvers.TryGetValue(resourceType, out var resolver))
        {
            _log.LogWarning("hasPermission cannot be evaluated: resourceType={ResourceType}, id={Id} (missing IScopeResolver?)",
                resourceType, targetId);
            return false;
        }
        var owner = await resolver.OwnerDepartmentOfAsync(id.Value, ct)
                    ?? throw new DomainException(SysErrorCode.SYS_NOT_FOUND, "Record not found");
        return await _roles.DataScopeOfAsync(user.Roles, ct) == DataScope.ALL || owner == user.DepartmentId;
    }

    private static Guid? ToGuid(object? targetId) => targetId switch
    {
        Guid guid => guid,
        null => null,
        _ => Guid.TryParse(targetId.ToString(), out var parsed) ? parsed : null,
    };
}
