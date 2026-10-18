using System.Globalization;
using System.Text;
using Dapper;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using PortFlow.Identity.Api;
using PortFlow.Shared.Persistence;
using PortFlow.Shared.Security;
using PortFlow.Shared.Time;
using PortFlow.Shared.Web;
using PortFlow.Workflow.Infrastructure;

namespace PortFlow.Workflow.Application;

public enum InboxTab
{
    ASSIGNED,
    SUBMITTED,
    WATCHING,
}

public sealed record InboxItem(Guid InstanceId, string DocumentType, Guid DocumentId, string Status, int Round,
    string? Number, string? Title, string? CustomerName, decimal? Amount, string? Currency, Guid SubmitterId,
    string? SubmitterName, Instant SubmittedAt, Guid? StepId, int? StepOrder, string? StepName, Instant? StartedAt,
    Instant? DueAt, bool Overdue);

public sealed record InboxCounter(string DocumentType, long Total, long Overdue);

public sealed record InboxCounters(long Total, long Overdue, IReadOnlyList<InboxCounter> ByDocumentType);

/// <summary>Hộp việc (CQRS nhẹ – SQL trực tiếp): việc chờ tôi duyệt, hồ sơ tôi gửi, hồ sơ tôi đã tham gia.</summary>
public sealed class InboxQueryService(
    DbSession session,
    WorkflowDbContext db,
    IDelegationQuery delegations,
    IUserDirectory users,
    IBusinessClock clock)
{
    private const string Select = """
        SELECT s.id AS StepId, s.name AS StepName, s.step_order AS StepOrder, s.started_at AS StartedAt, s.due_at AS DueAt,
               i.id AS InstanceId, i.document_type AS DocumentType, i.document_id AS DocumentId, i.status AS Status,
               i.round AS Round, i.submitter_id AS SubmitterId, i.submitted_at AS SubmittedAt,
               i.context_vars -> '_summary' ->> 'number' AS Number,
               i.context_vars -> '_summary' ->> 'title' AS Title,
               i.context_vars -> '_summary' ->> 'customerName' AS CustomerName,
               i.context_vars -> '_summary' ->> 'amount' AS Amount,
               i.context_vars -> '_summary' ->> 'currency' AS Currency
        """;

    public async Task<PageResponse<InboxItem>> InboxAsync(ICurrentUser user, InboxTab tab, string? documentType,
        bool overdueOnly, int page, int size, CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        parameters.Add("userId", user.Id);
        parameters.Add("now", clock.Now());
        var where = new StringBuilder();
        string from;
        string order;
        switch (tab)
        {
            case InboxTab.ASSIGNED:
                from = " FROM workflow.step_instance s JOIN workflow.workflow_instance i ON i.id = s.instance_id";
                where.Append(" WHERE s.status = 'PENDING' AND ").Append(await AssignedToAsync(user, parameters, ct));
                order = " ORDER BY s.due_at NULLS LAST, s.started_at, s.id";
                break;
            case InboxTab.SUBMITTED:
                from = PendingStepJoin;
                where.Append(" WHERE i.submitter_id = @userId");
                order = " ORDER BY i.submitted_at DESC, i.id";
                break;
            default:
                from = PendingStepJoin;
                where.Append("""
                     WHERE EXISTS (SELECT 1 FROM workflow.approval_action a
                         JOIN workflow.step_instance sa ON sa.id = a.step_instance_id
                         WHERE sa.instance_id = i.id AND a.user_id = @userId)
                    """);
                order = " ORDER BY i.submitted_at DESC, i.id";
                break;
        }
        if (!string.IsNullOrWhiteSpace(documentType))
        {
            where.Append(" AND i.document_type = @documentType");
            parameters.Add("documentType", documentType);
        }
        if (overdueOnly)
        {
            where.Append(" AND s.due_at < @now");
        }
        var connection = await session.OpenAsync(ct);
        var total = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*)" + from + where, parameters, session.Transaction, cancellationToken: ct));
        parameters.Add("limit", size);
        parameters.Add("offset", (long)page * size);
        var rows = (await connection.QueryAsync<Row>(new CommandDefinition(
            Select + from + where + order + " LIMIT @limit OFFSET @offset", parameters, session.Transaction,
            cancellationToken: ct))).ToList();
        var names = await users.FullNamesAsync(rows.Select(row => row.SubmitterId), ct);
        var now = clock.Now();
        var items = rows.Select(row => row.ToItem(names.GetValueOrDefault(row.SubmitterId), now)).ToList();
        return new PageResponse<InboxItem>(items, total, page, size);
    }

    public async Task<InboxCounters> CountersAsync(ICurrentUser user, CancellationToken ct)
    {
        var parameters = new DynamicParameters();
        parameters.Add("userId", user.Id);
        parameters.Add("now", clock.Now());
        var sql = """
            SELECT i.document_type AS DocumentType, count(*) AS Total, count(*) FILTER (WHERE s.due_at < @now) AS Overdue
            FROM workflow.step_instance s JOIN workflow.workflow_instance i ON i.id = s.instance_id
            WHERE s.status = 'PENDING' AND
            """ + await AssignedToAsync(user, parameters, ct) + """
             GROUP BY i.document_type ORDER BY i.document_type
            """;
        var connection = await session.OpenAsync(ct);
        var byType = (await connection.QueryAsync<InboxCounter>(new CommandDefinition(sql, parameters,
            session.Transaction, cancellationToken: ct))).ToList();
        return new InboxCounters(byType.Sum(c => c.Total), byType.Sum(c => c.Overdue), byType);
    }

    /// <summary>
    /// Bản SQL của <c>StepActorPolicy.HasPendingVote</c>: còn ít nhất 1 người tôi đại diện (chính tôi hoặc người ủy quyền,
    /// trừ khi tôi là người gửi) chưa bỏ phiếu. Sửa quy tắc thì sửa cả hai nơi – DelegatedApprovalIT đối chiếu.
    /// </summary>
    private async Task<string> AssignedToAsync(ICurrentUser user, DynamicParameters parameters, CancellationToken ct)
    {
        var clause = new StringBuilder(" i.submitter_id <> @userId")
            .Append(" AND ((s.assignee_user_ids @> ARRAY[@userId]::uuid[] AND ").Append(NotVoted("@userId")).Append(')');
        var index = 0;
        var types = await db.Definitions.Select(d => d.DocumentType).Distinct().ToListAsync(ct);
        foreach (var type in types)
        {
            var delegators = await delegations.ActiveDelegatorsOfAsync(user.Id, type, clock.Today(), ct);
            if (delegators.Count == 0)
            {
                continue;
            }
            clause.Append($" OR (i.document_type = @dt{index}")
                .Append(" AND EXISTS (SELECT 1 FROM unnest(s.assignee_user_ids) AS d(id)")
                .Append($" WHERE d.id = ANY (@dg{index}) AND ").Append(NotVoted("d.id")).Append("))");
            parameters.Add($"dt{index}", type);
            parameters.Add($"dg{index}", delegators.ToArray());
            index++;
        }
        return clause.Append(')').ToString();
    }

    private static string NotVoted(string voter) =>
        "NOT EXISTS (SELECT 1 FROM workflow.approval_action a WHERE a.step_instance_id = s.id"
        + " AND COALESCE(a.on_behalf_of, a.user_id) = " + voter + ")";

    private const string PendingStepJoin = " FROM workflow.workflow_instance i"
                                           + " LEFT JOIN workflow.step_instance s ON s.instance_id = i.id AND s.status = 'PENDING'";

    private sealed record Row
    {
        public Guid? StepId { get; init; }

        public string? StepName { get; init; }

        public int? StepOrder { get; init; }

        public Instant? StartedAt { get; init; }

        public Instant? DueAt { get; init; }

        public Guid InstanceId { get; init; }

        public string DocumentType { get; init; } = null!;

        public Guid DocumentId { get; init; }

        public string Status { get; init; } = null!;

        public int Round { get; init; }

        public Guid SubmitterId { get; init; }

        public Instant SubmittedAt { get; init; }

        public string? Number { get; init; }

        public string? Title { get; init; }

        public string? CustomerName { get; init; }

        public string? Amount { get; init; }

        public string? Currency { get; init; }

        public InboxItem ToItem(string? submitterName, Instant now) =>
            new(InstanceId, DocumentType, DocumentId, Status, Round, Number, Title, CustomerName,
                Amount is null ? null : decimal.Parse(Amount, NumberStyles.Float, CultureInfo.InvariantCulture), Currency,
                SubmitterId, submitterName, SubmittedAt, StepId, StepOrder, StepName, StartedAt, DueAt,
                DueAt is { } due && due < now);
    }
}
