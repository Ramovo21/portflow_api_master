using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PortFlow.Identity.Api;
using PortFlow.Identity.Application;
using PortFlow.Identity.Domain;
using PortFlow.Shared.Idempotency;
using PortFlow.Shared.Security;

namespace PortFlow.Identity.Web;

[ApiController]
[Route("api/v1/admin/departments")]
public sealed class DepartmentController(DepartmentService departments) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permission.USER_ADMIN)]
    public async Task<IReadOnlyList<DepartmentResponse>> List() =>
        (await departments.FindAllAsync(HttpContext.RequestAborted)).Select(DepartmentResponse.Of).ToList();

    [HttpGet("{id:guid}")]
    [RequirePermission(Permission.USER_ADMIN)]
    public async Task<DepartmentResponse> Get(Guid id) =>
        DepartmentResponse.Of(await departments.GetAsync(id, HttpContext.RequestAborted));

    [HttpPost]
    [Idempotent]
    [RequirePermission(Permission.USER_ADMIN)]
    public async Task<ActionResult<DepartmentResponse>> Create([FromBody] DepartmentRequest request) =>
        StatusCode(StatusCodes.Status201Created,
            DepartmentResponse.Of(await departments.CreateAsync(request.ToCommand(), HttpContext.RequestAborted)));

    [HttpPut("{id:guid}")]
    [RequirePermission(Permission.USER_ADMIN)]
    public async Task<DepartmentResponse> Update(Guid id, [FromBody] DepartmentRequest request) =>
        DepartmentResponse.Of(await departments.UpdateAsync(id, request.ToCommand(), HttpContext.RequestAborted));

    public sealed record DepartmentRequest(string Code, string Name, Guid? HeadUserId, Guid? ParentId)
    {
        public DepartmentCommand ToCommand() => new(Code, Name, HeadUserId, ParentId);
    }

    public sealed class DepartmentRequestValidator : AbstractValidator<DepartmentRequest>
    {
        public DepartmentRequestValidator()
        {
            RuleFor(r => r.Code).NotEmpty().MaximumLength(32);
            RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        }
    }

    public sealed record DepartmentResponse(Guid Id, string Code, string Name, Guid? HeadUserId, Guid? ParentId,
        long Version)
    {
        public static DepartmentResponse Of(Department d) => new(d.Id, d.Code, d.Name, d.HeadUserId, d.ParentId, d.Version);
    }
}

[ApiController]
[Route("api/v1/admin")]
public sealed class RoleAdminController(RoleAdminService roles) : ControllerBase
{
    [HttpGet("roles")]
    [RequirePermission(Permission.ROLE_ADMIN)]
    public async Task<IReadOnlyList<RoleResponse>> List() =>
        (await roles.FindAllAsync(HttpContext.RequestAborted)).Select(RoleResponse.Of).ToList();

    [HttpGet("roles/{code}")]
    [RequirePermission(Permission.ROLE_ADMIN)]
    public async Task<RoleResponse> Get(string code) => RoleResponse.Of(await roles.GetAsync(code, HttpContext.RequestAborted));

    [HttpPut("roles/{code}")]
    [RequirePermission(Permission.ROLE_ADMIN)]
    public async Task<RoleResponse> Update(string code, [FromBody] RoleRequest request) =>
        RoleResponse.Of(await roles.UpdateAsync(code, request.Name, request.Permissions!, request.DataScope!.Value,
            HttpContext.RequestAborted));

    [HttpGet("permissions")]
    [RequirePermission(Permission.ROLE_ADMIN)]
    public IReadOnlyList<string> Permissions() => Permission.All;

    public sealed record RoleRequest(string Name, IReadOnlySet<string>? Permissions, DataScope? DataScope);

    public sealed class RoleRequestValidator : AbstractValidator<RoleRequest>
    {
        public RoleRequestValidator()
        {
            RuleFor(r => r.Name).NotEmpty();
            RuleFor(r => r.Permissions).NotNull();
            RuleFor(r => r.DataScope).NotNull();
        }
    }

    public sealed record RoleResponse(string Code, string Name, IReadOnlyList<string> Permissions, DataScope DataScope,
        bool System, long Version)
    {
        public static RoleResponse Of(Role r) =>
            new(r.Code, r.Name, r.Permissions.Distinct().Order(StringComparer.Ordinal).ToList(), r.DataScope, r.System,
                r.Version);
    }
}

[ApiController]
[Route("api/v1/admin/document-types")]
public sealed class DocumentTypeController(DocumentTypeService documentTypes) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permission.DOCUMENT_TYPE_ADMIN)]
    public async Task<IReadOnlyList<DocumentTypeResponse>> List() =>
        (await documentTypes.FindAllAsync(HttpContext.RequestAborted)).Select(DocumentTypeResponse.Of).ToList();

    [HttpGet("{code}")]
    [RequirePermission(Permission.DOCUMENT_TYPE_ADMIN)]
    public async Task<DocumentTypeResponse> Get(string code) =>
        DocumentTypeResponse.Of(await documentTypes.FindAsync(code, HttpContext.RequestAborted));

    [HttpPut("{code}")]
    [RequirePermission(Permission.DOCUMENT_TYPE_ADMIN)]
    public async Task<DocumentTypeResponse> Update(string code, [FromBody] DocumentTypeRequest request)
    {
        var requirements = request.SubmitRequirements is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } json
            ? json.GetRawText()
            : "{}";
        return DocumentTypeResponse.Of(await documentTypes.UpdateAsync(code, request.Name, request.RequiresESign,
            request.WorkflowKey, request.Active, requirements, HttpContext.RequestAborted));
    }

    public sealed record DocumentTypeRequest(string Name, bool RequiresESign, string WorkflowKey, bool Active,
        JsonElement? SubmitRequirements);

    public sealed class DocumentTypeRequestValidator : AbstractValidator<DocumentTypeRequest>
    {
        public DocumentTypeRequestValidator()
        {
            RuleFor(r => r.Name).NotEmpty();
            RuleFor(r => r.WorkflowKey).NotEmpty();
        }
    }

    public sealed record DocumentTypeResponse(string Code, string Name, bool RequiresESign, string WorkflowKey,
        bool Active, JsonElement SubmitRequirements)
    {
        public static DocumentTypeResponse Of(DocumentType t) =>
            new(t.Code, t.Name, t.RequiresEsign, t.WorkflowKey, t.Active,
                JsonDocument.Parse(t.SubmitRequirements).RootElement.Clone());
    }
}

[ApiController]
[Route("api/v1/admin/users")]
public sealed class UserAdminController(IdentityQueries queries, KeycloakSyncService sync) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permission.USER_ADMIN)]
    public Task<IReadOnlyList<UserRow>> Users([FromQuery] string? q, [FromQuery] Guid? departmentId,
        [FromQuery] string? role, [FromQuery] bool? active) =>
        queries.UsersAsync(q, departmentId, role, active, HttpContext.RequestAborted);

    [HttpPost("sync")]
    [RequirePermission(Permission.USER_ADMIN)]
    public Task<SyncResult> Sync() => sync.SyncAllAsync(KeycloakUserSnapshot.MANUAL, HttpContext.RequestAborted);
}

[ApiController]
[Route("api/v1/users/lookup")]
public sealed class UserLookupController(IUserDirectory users, IdentityQueries queries) : ControllerBase
{
    [HttpGet]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public Task<IReadOnlyList<UserRef>> Lookup([FromQuery(Name = "role")] string? role, [FromQuery] Guid? departmentId)
    {
        if (string.IsNullOrEmpty(role))
        {
            throw new BadHttpRequestException("Required parameter 'role' is not present");
        }
        return departmentId is { } department
            ? users.FindActiveByRoleInDepartmentAsync(role, department, HttpContext.RequestAborted)
            : users.FindActiveByRoleAsync(role, HttpContext.RequestAborted);
    }

    /// <summary>Người có thể nhận ủy quyền duyệt (đang hoạt động, có Workflow.Act).</summary>
    [HttpGet("approvers")]
    [RequirePermission(Permission.DELEGATION_MANAGE)]
    public Task<IReadOnlyList<UserOption>> Approvers([FromQuery] string? q) =>
        queries.ActiveUsersHoldingAsync(Permission.WORKFLOW_ACT, q, 20, HttpContext.RequestAborted);
}
