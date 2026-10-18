using Microsoft.EntityFrameworkCore;
using Npgsql;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Persistence;
using PortFlow.Workflow.Domain;

namespace PortFlow.Workflow.Infrastructure;

internal sealed class WorkflowConstraintErrorMapping : IConstraintErrorMapping
{
    internal static readonly IReadOnlyDictionary<string, IErrorCode> Codes = new Dictionary<string, IErrorCode>
    {
        ["uq_wi_active_per_document"] = WorkflowError.WF_ALREADY_RUNNING,
        ["uq_aa_one_decisive"] = WorkflowError.WF_CONFLICT,
        ["uq_aa_step_user"] = WorkflowError.WF_CONFLICT,
        ["uq_aa_step_terminal"] = WorkflowError.WF_CONFLICT,
        ["uq_si_one_pending"] = WorkflowError.WF_CONFLICT,
        ["uq_si_order_round"] = WorkflowError.WF_CONFLICT,
        ["uq_wd_one_active"] = WorkflowError.WF_CONFLICT,
        ["uq_wd_type_version"] = WorkflowError.WF_CONFLICT,
        ["ck_aa_comment"] = WorkflowError.WF_COMMENT_REQUIRED,
        ["ck_si_pending_has_assignee"] = WorkflowError.WF_NO_ASSIGNEE,
    };

    public IReadOnlyDictionary<string, IErrorCode> Mappings => Codes;
}

/// <summary>
/// Ghi xuống DB và dịch lỗi đồng thời thành mã workflow: version lệch → WF_CONFLICT; vi phạm ràng buộc
/// workflow (2 phiếu quyết định, 2 instance chạy…) → mã tương ứng.
/// </summary>
public sealed class WorkflowExceptionTranslator
{
    public async Task SaveAsync(DbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(WorkflowError.WF_CONFLICT, "Document was just handled by someone else, please reload");
        }
        catch (DbUpdateException ex) when (ConstraintCode(ex) is { } code)
        {
            throw new DomainException(code, Equals(code, WorkflowError.WF_CONFLICT)
                ? "Step was already handled by someone else, please reload"
                : code.Title);
        }
    }

    private static IErrorCode? ConstraintCode(Exception ex)
    {
        for (var current = ex.InnerException; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { ConstraintName: { } name })
            {
                return WorkflowConstraintErrorMapping.Codes.GetValueOrDefault(name);
            }
        }
        return null;
    }
}
