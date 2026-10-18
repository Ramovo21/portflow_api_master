using System.Collections.Concurrent;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Idempotency;
using PortFlow.Shared.Id;
using PortFlow.Shared.Persistence;
using PortFlow.Shared.Security;
using PortFlow.Workflow.Api;
using PortFlow.Workflow.Application;
using PortFlow.Workflow.Domain;
using PortFlow.Workflow.Domain.Definition;
using PortFlow.Workflow.Infrastructure;

namespace PortFlow.Workflow.Demo;

/// <summary>
/// CHỈ DÙNG KHI PHÁT TRIỂN (không có bên Java): loại hồ sơ <c>DEMO</c> để thử trọn luồng duyệt F03 trên UI khi
/// chưa port module hồ sơ nào. Bật bằng <c>Portflow:Workflow:Demo:Enabled=true</c> (env <c>WORKFLOW_DEMO_ENABLED</c>).
/// </summary>
public sealed class WorkflowDemoOptions
{
    public const string Section = "Portflow:Workflow:Demo";

    public bool Enabled { get; set; }
}

/// <summary>Handler giả: tóm tắt hồ sơ giữ trong bộ nhớ (context_vars đã lưu bản tóm tắt nên restart không sao).</summary>
internal sealed class DemoDocumentHandler(ILogger<DemoDocumentHandler> log) : IApprovableDocumentHandler
{
    public const string Type = "DEMO";

    private readonly ConcurrentDictionary<Guid, DocumentSummary> _documents = new();

    public string DocumentType => Type;

    public void Register(Guid documentId, DocumentSummary summary) => _documents[documentId] = summary;

    public Task OnStepStartedAsync(Guid documentId, int stepOrder, CancellationToken ct)
    {
        log.LogInformation("Demo document {DocumentId} entered step {StepOrder}", documentId, stepOrder);
        return Task.CompletedTask;
    }

    public Task OnApprovedAsync(Guid documentId, ApprovalOutcome outcome, CancellationToken ct)
    {
        log.LogInformation("Demo document {DocumentId} approved (eSign={ESign})", documentId, outcome.RequiresESign);
        return Task.CompletedTask;
    }

    public Task OnRejectedAsync(Guid documentId, string? comment, CancellationToken ct)
    {
        log.LogInformation("Demo document {DocumentId} rejected: {Comment}", documentId, comment);
        return Task.CompletedTask;
    }

    public Task OnRevisionRequestedAsync(Guid documentId, string? comment, CancellationToken ct)
    {
        log.LogInformation("Demo document {DocumentId} sent back for revision: {Comment}", documentId, comment);
        return Task.CompletedTask;
    }

    public Task<DocumentSummary> SummarizeAsync(Guid documentId, CancellationToken ct) =>
        Task.FromResult(_documents.GetValueOrDefault(documentId)
                        ?? new DocumentSummary("DEMO-" + documentId.ToString()[..8], "Hồ sơ demo", null, null, null));
}

public sealed record DemoSubmission(string? Title, string? CustomerName, decimal? Amount);

public sealed record DemoSubmitted(Guid DocumentId, WorkflowStartResult Workflow);

public sealed record DemoStatus(bool Enabled, string DocumentType);

/// <summary>Đảm bảo có loại hồ sơ DEMO + định nghĩa ACTIVE rồi trình duyệt một hồ sơ demo dưới tên người gọi.</summary>
internal sealed class WorkflowDemoService(
    DbSession session,
    WorkflowDbContext db,
    DefinitionAdminService definitions,
    IWorkflowService workflow,
    DemoDocumentHandler handler)
{
    private const string Definition = """
        {"documentType": "DEMO", "resubmitPolicy": "RESTART",
         "steps": [
           {"order": 1, "name": "Trưởng phòng Kinh doanh",
            "assignee": {"type": "ROLE", "value": "SALES_MANAGER", "scope": "SAME_DEPARTMENT_AS_SUBMITTER"},
            "onReject": "BACK_TO_SUBMITTER", "slaHours": 24},
           {"order": 2, "name": "Pháp chế", "assignee": {"type": "ROLE", "value": "LEGAL"},
            "onReject": "TERMINATE", "condition": "amount > 500000000", "slaHours": 48},
           {"order": 3, "name": "Ban Giám đốc", "assignee": {"type": "GROUP", "value": "DIRECTOR"},
            "onReject": "TERMINATE", "slaHours": 48}],
         "onFinalApprove": {"emitESign": false}}
        """;

    public async Task<DemoSubmitted> SubmitAsync(ICurrentUser user, DemoSubmission submission, CancellationToken ct)
    {
        await EnsureSetUpAsync(ct);
        var documentId = UuidV7.Next();
        var amount = submission.Amount ?? 300000000m;
        handler.Register(documentId, new DocumentSummary("DEMO-" + documentId.ToString()[^6..].ToUpperInvariant(),
            string.IsNullOrWhiteSpace(submission.Title) ? "Hồ sơ demo" : submission.Title.Trim(),
            string.IsNullOrWhiteSpace(submission.CustomerName) ? null : submission.CustomerName.Trim(), amount, "VND"));
        var started = await workflow.StartAsync(new StartWorkflowCommand(DemoDocumentHandler.Type, documentId, user.Id,
            new Dictionary<string, object?> { ["amount"] = amount }), ct);
        return new DemoSubmitted(documentId, started);
    }

    private async Task EnsureSetUpAsync(CancellationToken ct)
    {
        var connection = await session.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO identity.document_type (code, name, requires_esign, workflow_key)
            VALUES ('DEMO', 'Hồ sơ demo (dev)', false, 'DEMO') ON CONFLICT DO NOTHING
            """, transaction: session.Transaction, cancellationToken: ct));
        if (await db.Definitions.AnyAsync(d => d.DocumentType == DemoDocumentHandler.Type
                                               && d.Status == DefinitionStatus.ACTIVE, ct))
        {
            return;
        }
        var draft = await definitions.CreateAsync(SystemUser.Instance, WorkflowJson.ReadDefinition(Definition), null, ct);
        await definitions.ActivateAsync(SystemUser.Instance, draft.Definition.Id, ct);
    }
}

[ApiController]
[Route("api/v1/dev/workflow-demo")]
public sealed class WorkflowDemoController(IOptions<WorkflowDemoOptions> options, ICurrentUserProvider users)
    : ControllerBase
{
    [HttpGet]
    [Authorize]
    public ActionResult<DemoStatus> Status() =>
        options.Value.Enabled ? new DemoStatus(true, DemoDocumentHandler.Type) : NotFound();

    [HttpPost]
    [Idempotent]
    [Authorize]
    public async Task<ActionResult<DemoSubmitted>> Submit([FromBody] DemoSubmission submission)
    {
        if (!options.Value.Enabled)
        {
            return NotFound();
        }
        var service = HttpContext.RequestServices.GetService(typeof(WorkflowDemoService)) as WorkflowDemoService
                      ?? throw new InvalidOperationException("Workflow demo is not registered");
        return StatusCode(StatusCodes.Status201Created,
            await service.SubmitAsync(users.Current(), submission, HttpContext.RequestAborted));
    }
}
