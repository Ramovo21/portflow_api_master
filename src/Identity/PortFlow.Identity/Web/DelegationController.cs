using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using PortFlow.Identity.Api;
using PortFlow.Identity.Application;
using PortFlow.Identity.Domain;
using PortFlow.Shared.Idempotency;
using PortFlow.Shared.Security;

namespace PortFlow.Identity.Web;

[ApiController]
[Route("api/v1/delegations")]
public sealed class DelegationController(
    DelegationService delegations,
    IUserDirectory users,
    ICurrentUserProvider currentUser) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permission.DELEGATION_MANAGE)]
    public async Task<IReadOnlyList<DelegationResponse>> List()
    {
        var ct = HttpContext.RequestAborted;
        var visible = await delegations.VisibleToAsync(currentUser.Current(), ct);
        var names = await users.FullNamesAsync(visible.SelectMany(d => new[] { d.FromUserId, d.ToUserId }), ct);
        return visible.Select(d => DelegationResponse.Of(d, names)).ToList();
    }

    [HttpPost]
    [Idempotent]
    [RequirePermission(Permission.DELEGATION_MANAGE)]
    public async Task<ActionResult<DelegationResponse>> Create([FromBody] DelegationRequest request)
    {
        var created = await delegations.CreateAsync(currentUser.Current(), new DelegationCommand(request.FromUserId,
            request.ToUserId, request.DocumentTypes, request.ValidFrom, request.ValidTo, request.Reason),
            HttpContext.RequestAborted);
        return StatusCode(StatusCodes.Status201Created, await RespondAsync(created));
    }

    [HttpPost("{id:guid}/revoke")]
    [Idempotent]
    [RequirePermission(Permission.DELEGATION_MANAGE)]
    public async Task<ActionResult<DelegationResponse>> Revoke(Guid id) =>
        Ok(await RespondAsync(await delegations.RevokeAsync(currentUser.Current(), id, HttpContext.RequestAborted)));

    private async Task<DelegationResponse> RespondAsync(Delegation delegation) =>
        DelegationResponse.Of(delegation,
            await users.FullNamesAsync([delegation.FromUserId, delegation.ToUserId], HttpContext.RequestAborted));

    public sealed record DelegationRequest(Guid? FromUserId, Guid? ToUserId, IReadOnlySet<string>? DocumentTypes,
        LocalDate? ValidFrom, LocalDate? ValidTo, string? Reason);

    public sealed record DelegationResponse(Guid Id, Guid FromUserId, string? FromUserName, Guid ToUserId,
        string? ToUserName, IReadOnlyCollection<string> DocumentTypes, LocalDate ValidFrom, LocalDate ValidTo,
        string Reason, bool Revoked)
    {
        public static DelegationResponse Of(Delegation d, IReadOnlyDictionary<Guid, string> names) =>
            new(d.Id, d.FromUserId, names.GetValueOrDefault(d.FromUserId), d.ToUserId,
                names.GetValueOrDefault(d.ToUserId), d.DocumentTypes, d.ValidFrom, d.ValidTo, d.Reason, d.Revoked);
    }
}
