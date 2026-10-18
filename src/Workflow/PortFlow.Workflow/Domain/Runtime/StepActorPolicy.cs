namespace PortFlow.Workflow.Domain.Runtime;

/// <summary>
/// APR-01 + FR-W04: ai được thao tác ở một bước và bỏ phiếu cho ai. Nguồn duy nhất cho engine, stepAuth
/// và màn chi tiết; câu SQL inbox (<c>InboxQueryService.AssignedTo</c>) là bản dịch của <see cref="HasPendingVote"/>.
/// <para><c>delegators</c> là các user đang ủy quyền cho actor với loại hồ sơ của instance (do tầng application tra).
/// Người gửi hồ sơ không bao giờ đại diện được ai, kể cả khi được ủy quyền (WF-X1).</para>
/// </summary>
public static class StepActorPolicy
{
    /// <summary>Các assignee mà actor đại diện: chính mình trước (nếu được giao), sau đó người ủy quyền theo thứ tự giao.</summary>
    public static IReadOnlyList<Guid> Represented(IReadOnlyCollection<Guid> assignees, Guid submitterId, Guid? actorId,
        IReadOnlySet<Guid> delegators)
    {
        if (actorId is not { } actor || actor == submitterId)
        {
            return [];
        }
        var result = new List<Guid>();
        if (assignees.Contains(actor))
        {
            result.Add(actor);
        }
        result.AddRange(assignees.Where(assignee => assignee != actor && delegators.Contains(assignee)));
        return result;
    }

    public static bool MayAct(IReadOnlyCollection<Guid> assignees, Guid submitterId, Guid? actorId,
        IReadOnlySet<Guid> delegators) =>
        Represented(assignees, submitterId, actorId, delegators).Count > 0;

    /// <summary>Còn ít nhất một người actor đại diện chưa bỏ phiếu – điều kiện để bước nằm trong inbox / hiện nút duyệt.</summary>
    public static bool HasPendingVote(StepInstance step, Guid submitterId, Guid? actorId, IReadOnlySet<Guid> delegators) =>
        Represented(step.AssigneeUserIds, submitterId, actorId, delegators).Any(voter => !step.HasVoteFrom(voter));

    /// <summary>
    /// Người mà phiếu tiếp theo của actor được tính cho. Nếu mọi người actor đại diện đã bỏ phiếu thì trả người đầu
    /// tiên để aggregate báo WF_ALREADY_VOTED; null nghĩa là actor không có quyền ở bước này.
    /// </summary>
    public static Guid? NextVoter(StepInstance step, Guid submitterId, Guid? actorId, IReadOnlySet<Guid> delegators)
    {
        var voters = Represented(step.AssigneeUserIds, submitterId, actorId, delegators);
        if (voters.Count == 0)
        {
            return null;
        }
        foreach (var voter in voters)
        {
            if (!step.HasVoteFrom(voter))
            {
                return voter;
            }
        }
        return voters[0];
    }
}
