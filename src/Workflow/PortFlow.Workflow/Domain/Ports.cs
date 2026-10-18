using PortFlow.Workflow.Domain.Definition;

namespace PortFlow.Workflow.Domain;

/// <summary>Đánh giá điều kiện của bước (SpEL rút gọn) trên biến ngữ cảnh đã lọc theo <see cref="Variables"/>.</summary>
public interface IConditionEvaluator
{
    static readonly IReadOnlySet<string> Variables = new HashSet<string>
    {
        "docType", "amount", "currency", "customerTier", "department", "isAdjustment", "addendumType", "scope",
        "serviceType", "maxDeltaPct",
    };

    /// <summary>Điều kiện rỗng = luôn áp dụng. Lỗi cú pháp / không trả bool → DomainException(WF_CONDITION_ERROR).</summary>
    bool Evaluate(string? expression, IReadOnlyDictionary<string, object?> vars);
}

public sealed record ResolveContext(Guid? SubmitterId, Guid? SubmitterDepartmentId, string DocumentType,
    IReadOnlyDictionary<string, object?> Vars);

public interface IAssigneeResolver
{
    Task<IReadOnlyList<Guid>> ResolveAsync(AssigneeRule rule, ResolveContext context, CancellationToken ct);
}

public interface IAssigneeDirectory
{
    /// <summary>null = role không tồn tại.</summary>
    Task<IReadOnlySet<string>?> PermissionsOfRoleAsync(string roleCode, CancellationToken ct);

    Task<bool> IsActiveUserAsync(Guid userId, CancellationToken ct);
}
