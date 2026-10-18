using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NodaTime;
using PortFlow.Identity.Api;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Idempotency;
using PortFlow.Shared.Security;
using PortFlow.Shared.Web;
using PortFlow.Workflow.Api;
using PortFlow.Workflow.Application;
using PortFlow.Workflow.Domain.Runtime;

namespace PortFlow.Workflow.Web;

[ApiController]
[Route("api/v1/workflow-instances")]
public sealed class WorkflowInstanceController(
    WorkflowEngine engine,
    InstanceQueryService queries,
    IUserDirectory directory,
    ICurrentUserProvider users) : ControllerBase
{
    [HttpGet("{id:guid}")]
    [Authorize]
    public Task<InstanceView> Detail(Guid id) => queries.DetailAsync(id, users.Current(), HttpContext.RequestAborted);

    [HttpGet]
    [Authorize]
    public Task<IReadOnlyList<InstanceView>> ByDocument([FromQuery, BindRequired] string documentType,
        [FromQuery, BindRequired] Guid documentId) =>
        queries.ByDocumentAsync(documentType, documentId, users.Current(), HttpContext.RequestAborted);

    [HttpPost("{id:guid}/steps/{stepId:guid}/approve")]
    [Idempotent]
    [Authorize]
    [TypeFilter(typeof(StepActorFilter))]
    public Task<ActionResult<ActionResponse>> Approve(Guid id, Guid stepId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ActionRequest? request) =>
        ActAsync(id, stepId, ActionType.APPROVE, request);

    [HttpPost("{id:guid}/steps/{stepId:guid}/reject")]
    [Idempotent]
    [Authorize]
    [TypeFilter(typeof(StepActorFilter))]
    public Task<ActionResult<ActionResponse>> Reject(Guid id, Guid stepId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ActionRequest? request) =>
        ActAsync(id, stepId, ActionType.REJECT, request);

    [HttpPost("{id:guid}/steps/{stepId:guid}/request-revision")]
    [Idempotent]
    [Authorize]
    [TypeFilter(typeof(StepActorFilter))]
    public Task<ActionResult<ActionResponse>> RequestRevision(Guid id, Guid stepId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ActionRequest? request) =>
        ActAsync(id, stepId, ActionType.REQUEST_REVISION, request);

    private async Task<ActionResult<ActionResponse>> ActAsync(Guid instanceId, Guid stepId, ActionType action,
        ActionRequest? request)
    {
        var body = request ?? new ActionRequest(null, null);
        var result = await engine.ActAsync(new ActOnStepCommand(instanceId, stepId, users.Current().Id, action,
            body.Comment, body.ExpectedVersion), HttpContext.RequestAborted);
        return Ok(await ActionResponse.OfAsync(result, directory, HttpContext.RequestAborted));
    }

    public sealed record ActionRequest(string? Comment, long? ExpectedVersion);

    public sealed record CompletedStep(int Order, string Name, string? Decision);

    public sealed record Assignee(Guid Id, string? FullName);

    public sealed record CurrentStep(Guid Id, int Order, string Name, IReadOnlyList<Assignee> Assignees, Instant? DueAt);

    public sealed record ActionResponse(Guid InstanceId, string Status, long? Version, CompletedStep? CompletedStep,
        CurrentStep? CurrentStep)
    {
        public static async Task<ActionResponse> OfAsync(StepActionResult result, IUserDirectory users, CancellationToken ct)
        {
            CurrentStep? currentStep = null;
            if (result.CurrentStep is { } current)
            {
                var names = await users.FullNamesAsync(current.AssigneeUserIds, ct);
                currentStep = new CurrentStep(current.Id, current.StepOrder, current.Name,
                    current.AssigneeUserIds.Select(id => new Assignee(id, names.GetValueOrDefault(id))).ToList(),
                    current.DueAt);
            }
            var done = result.CompletedStep;
            return new ActionResponse(result.InstanceId, result.Status, result.Version,
                done is null ? null : new CompletedStep(done.StepOrder, done.Name, done.Decision?.ToString()),
                currentStep);
        }
    }
}

/// <summary>= <c>@PreAuthorize("@stepAuth.canAct(#stepId, authentication)")</c>: chặn trước khi vào action (và idempotency).</summary>
internal sealed class StepActorFilter(IStepAuthorization stepAuth, ICurrentUserProvider users) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!Guid.TryParse(context.RouteData.Values.GetValueOrDefault("stepId")?.ToString(), out var stepId)
            || !await stepAuth.CanActAsync(stepId, users.Current(), context.HttpContext.RequestAborted))
        {
            var problem = Problems.Of(new ErrorCode("AUTH_FORBIDDEN", System.Net.HttpStatusCode.Forbidden,
                "Not allowed to perform this action"), "You do not have permission to perform this action");
            Problems.Enrich(context.HttpContext, problem);
            context.Result = new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status403Forbidden,
                ContentTypes = { Problems.ContentType },
            };
        }
    }
}
