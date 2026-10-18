using NodaTime;
using PortFlow.Shared.Id;

namespace PortFlow.Workflow.Domain.Runtime;

/// <summary>Một phiếu ở một bước. <see cref="OnBehalfOf"/> = assignee gốc khi duyệt thay theo ủy quyền.</summary>
public sealed class ApprovalAction
{
    private ApprovalAction()
    {
    }

    internal ApprovalAction(Guid userId, Guid? onBehalfOf, ActionType action, string? comment, Instant at)
    {
        Id = UuidV7.Next();
        UserId = userId;
        OnBehalfOf = onBehalfOf;
        Action = action;
        Comment = comment;
        At = at;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid? OnBehalfOf { get; private set; }

    public ActionType Action { get; private set; }

    public string? Comment { get; private set; }

    public Instant At { get; private set; }

    /// <summary>Phiếu làm bước hoàn tất (DB: tối đa 1 / bước – chống duyệt trùng).</summary>
    public bool Decisive { get; private set; }

    /// <summary>Người mà phiếu này được tính cho.</summary>
    public Guid Voter => OnBehalfOf ?? UserId;

    internal void MarkDecisive() => Decisive = true;
}
