using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using PortFlow.Identity.Api;
using PortFlow.Shared.Idempotency;
using PortFlow.Shared.Security;
using PortFlow.Workflow.Application;
using PortFlow.Workflow.Domain;
using PortFlow.Workflow.Domain.Definition;

namespace PortFlow.Workflow.Web;

[ApiController]
[Route("api/v1/admin/workflow-definitions")]
public sealed class WorkflowDefinitionAdminController(DefinitionAdminService admin, ICurrentUserProvider users)
    : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permission.WORKFLOW_ADMIN)]
    public async Task<IReadOnlyList<DefinitionResponse>> List([FromQuery] string? documentType,
        [FromQuery] DefinitionStatus? status) =>
        (await admin.ListAsync(documentType, status, HttpContext.RequestAborted))
        .Select(definition => DefinitionResponse.Of(definition, [])).ToList();

    [HttpGet("{id:guid}")]
    [RequirePermission(Permission.WORKFLOW_ADMIN)]
    public async Task<DefinitionResponse> Get(Guid id) =>
        DefinitionResponse.Of(await admin.GetAsync(id, HttpContext.RequestAborted), []);

    [HttpPost]
    [Idempotent]
    [RequirePermission(Permission.WORKFLOW_ADMIN)]
    public async Task<ActionResult<DefinitionResponse>> Create([FromBody] DefinitionContent definition,
        [FromQuery] LocalDate? effectiveFrom)
    {
        var saved = await admin.CreateAsync(users.Current(), definition, effectiveFrom, HttpContext.RequestAborted);
        return StatusCode(StatusCodes.Status201Created, DefinitionResponse.Of(saved.Definition, saved.Warnings));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permission.WORKFLOW_ADMIN)]
    public async Task<DefinitionResponse> Update(Guid id, [FromBody] DefinitionContent definition,
        [FromQuery] LocalDate? effectiveFrom)
    {
        var saved = await admin.UpdateAsync(users.Current(), id, definition, effectiveFrom, HttpContext.RequestAborted);
        return DefinitionResponse.Of(saved.Definition, saved.Warnings);
    }

    [HttpPost("{id:guid}/clone")]
    [Idempotent]
    [RequirePermission(Permission.WORKFLOW_ADMIN)]
    public async Task<ActionResult<DefinitionResponse>> CloneAsDraft(Guid id) =>
        StatusCode(StatusCodes.Status201Created,
            DefinitionResponse.Of(await admin.CloneAsDraftAsync(users.Current(), id, HttpContext.RequestAborted), []));

    [HttpPost("{id:guid}/validate")]
    [RequirePermission(Permission.WORKFLOW_ADMIN)]
    public async Task<ValidationResponse> Validate(Guid id)
    {
        var violations = await admin.ValidateAsync(id, HttpContext.RequestAborted);
        return new ValidationResponse(violations.All(v => !v.IsError), violations);
    }

    [HttpPost("{id:guid}/activate")]
    [Idempotent]
    [RequirePermission(Permission.WORKFLOW_ADMIN)]
    public async Task<ActionResult<ActivationResponse>> Activate(Guid id)
    {
        var activated = await admin.ActivateAsync(users.Current(), id, HttpContext.RequestAborted);
        return Ok(new ActivationResponse(DefinitionResponse.Of(activated.Definition, []),
            activated.RunningInstancesOnOldVersions));
    }

    [HttpPost("{id:guid}/simulate")]
    [RequirePermission(Permission.WORKFLOW_ADMIN)]
    public Task<Simulation> Simulate(Guid id, [FromBody] SimulateRequest request) =>
        admin.SimulateAsync(id, request.Context?.ToDictionary(e => e.Key, e => (object?)e.Value), request.SubmitterId,
            HttpContext.RequestAborted);

    [HttpGet("{id:guid}/running-instances")]
    [RequirePermission(Permission.WORKFLOW_ADMIN)]
    public async Task<RunningInstancesResponse> RunningInstances(Guid id) =>
        new(id, await admin.RunningInstancesAsync(id, HttpContext.RequestAborted));

    public sealed record SimulateRequest(Dictionary<string, JsonElement>? Context, Guid? SubmitterId);

    public sealed record DefinitionResponse(Guid Id, string DocumentType, int Version, DefinitionStatus Status,
        LocalDate? EffectiveFrom, DefinitionContent Definition, Instant? ActivatedAt, Instant CreatedAt,
        Instant? UpdatedAt, IReadOnlyList<Violation> Warnings)
    {
        public static DefinitionResponse Of(WorkflowDefinition d, IReadOnlyList<Violation> warnings) =>
            new(d.Id, d.DocumentType, d.Version, d.Status, d.EffectiveFrom, d.Content, d.ActivatedAt, d.CreatedAt,
                d.UpdatedAt, warnings);
    }

    public sealed record ValidationResponse(bool Valid, IReadOnlyList<Violation> Violations);

    public sealed record ActivationResponse(DefinitionResponse Definition, long RunningInstancesOnOldVersions);

    public sealed record RunningInstancesResponse(Guid DefinitionId, long Running);
}
