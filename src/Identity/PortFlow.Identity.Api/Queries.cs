using NodaTime;
using PortFlow.Shared.Security;

namespace PortFlow.Identity.Api;

/// <summary>Bộ lọc phạm vi dữ liệu cho truy vấn danh sách: null = không lọc (scope ALL hoặc hệ thống).</summary>
public interface IDataScopeQuery
{
    Task<Guid?> DepartmentFilterAsync(ICurrentUser user, CancellationToken ct = default);
}

public interface IDelegationQuery
{
    /// <summary>Những người đang ủy quyền cho <paramref name="delegateId"/> với loại hồ sơ vào ngày cho trước.</summary>
    Task<IReadOnlySet<Guid>> ActiveDelegatorsOfAsync(Guid delegateId, string documentType, LocalDate onDate,
        CancellationToken ct = default);
}

public sealed record DocumentTypeInfo(string Code, string Name, bool RequiresESign, string WorkflowKey, bool Active);

public interface IDocumentTypeQuery
{
    Task<DocumentTypeInfo> GetAsync(string code, CancellationToken ct = default);
}

/// <summary>Mỗi module hồ sơ đăng ký để tầng 2 (scope) đánh giá được quyền theo phòng ban sở hữu.</summary>
public interface IScopeResolver
{
    /// <summary>vd "CONTRACT"</summary>
    string ResourceType { get; }

    /// <summary>null = bản ghi không tồn tại.</summary>
    Task<Guid?> OwnerDepartmentOfAsync(Guid id, CancellationToken ct);
}

/// <summary>
/// Tầng 2 – scope dùng trong code (= <c>PermissionEvaluator.hasPermission(auth, id, type, action)</c>):
/// false nếu khác phòng ban (scope DEPARTMENT) hoặc không có <see cref="IScopeResolver"/> cho loại hồ sơ.
/// Bản ghi không tồn tại → <c>DomainException(SYS_NOT_FOUND)</c>.
/// </summary>
public interface IScopePermissionEvaluator
{
    Task<bool> HasPermissionAsync(ICurrentUser? user, object? targetId, string resourceType, CancellationToken ct = default);
}

/// <summary>
/// Tầng 2 – scope (= <c>hasPermission(#id, 'CONTRACT', 'read')</c>): đặt trên action, id lấy từ route value
/// <paramref name="routeParameter"/>. Kiểm sau permission; khác phòng ban (scope DEPARTMENT) → 403, không có bản ghi → 404.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireScopeAttribute(string resourceType, string routeParameter = "id", string action = "read")
    : Attribute
{
    public string ResourceType { get; } = resourceType;

    public string RouteParameter { get; } = routeParameter;

    public string Action { get; } = action;
}
