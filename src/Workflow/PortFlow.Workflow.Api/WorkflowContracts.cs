using NodaTime;
using PortFlow.Shared.Security;

namespace PortFlow.Workflow.Api;

/// <summary>
/// Module hồ sơ (hợp đồng, phụ lục, bảng giá…) cài đặt để workflow gọi ngược khi trạng thái duyệt thay đổi.
/// Chạy trong CÙNG transaction của engine – lỗi ở đây rollback cả thao tác duyệt.
/// </summary>
public interface IApprovableDocumentHandler
{
    string DocumentType { get; }

    Task OnStepStartedAsync(Guid documentId, int stepOrder, CancellationToken ct);

    Task OnApprovedAsync(Guid documentId, ApprovalOutcome outcome, CancellationToken ct);

    Task OnRejectedAsync(Guid documentId, string? comment, CancellationToken ct);

    Task OnRevisionRequestedAsync(Guid documentId, string? comment, CancellationToken ct);

    Task<DocumentSummary> SummarizeAsync(Guid documentId, CancellationToken ct);
}

public sealed record ApprovalOutcome(Guid InstanceId, Instant ApprovedAt, bool RequiresESign);

public sealed record DocumentSummary(string? Number, string? Title, string? CustomerName, decimal? Amount,
    string? Currency)
{
    public static DocumentSummary Empty() => new(null, null, null, null, null);
}

public sealed record StartWorkflowCommand(string DocumentType, Guid DocumentId, Guid SubmitterId,
    IReadOnlyDictionary<string, object?>? ContextVars = null);

public sealed record WorkflowStartResult(Guid InstanceId, string Status, int Round, Guid? CurrentStepId,
    int? CurrentStepOrder, string? CurrentStepName, IReadOnlyList<Guid> AssigneeIds, Instant? DueAt);

public sealed record WorkflowProgress(Guid InstanceId, string Status, int Round, int? CurrentStepOrder,
    string? CurrentStepName, IReadOnlyList<Guid> CurrentAssignees, int TotalSteps, int DoneSteps);

public sealed record WorkflowHistoryEntry(Guid InstanceId, Instant At, int Round, int StepOrder, string StepName,
    string Action, Guid ActorId, Guid? OnBehalfOf, string? Comment);

/// <summary>Cổng vào workflow cho module hồ sơ (gọi trong transaction của module).</summary>
public interface IWorkflowService
{
    Task<WorkflowStartResult> StartAsync(StartWorkflowCommand command, CancellationToken ct = default);

    Task<WorkflowStartResult> ResubmitAsync(string documentType, Guid documentId, Guid userId,
        IReadOnlyDictionary<string, object?>? contextVars, CancellationToken ct = default);

    Task<WorkflowProgress?> ProgressOfAsync(string documentType, Guid documentId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, WorkflowProgress>> ProgressOfAsync(string documentType,
        IReadOnlyCollection<Guid> documentIds, CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowHistoryEntry>> HistoryOfAsync(string documentType, Guid documentId,
        CancellationToken ct = default);

    /// <summary>Hủy instance đang chạy; <paramref name="userId"/> null = hệ thống (vd hồ sơ bị hủy).</summary>
    Task CancelAsync(string documentType, Guid documentId, Guid? userId, string? reason, CancellationToken ct = default);
}

/// <summary>= <c>@stepAuth.canAct(#stepId, authentication)</c>: user có được thao tác ở bước này (kể cả ủy quyền).</summary>
public interface IStepAuthorization
{
    Task<bool> CanActAsync(Guid stepId, ICurrentUser user, CancellationToken ct = default);
}
