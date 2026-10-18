using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using PortFlow.Identity.Api;
using PortFlow.Identity.Application;
using PortFlow.Identity.Domain;
using PortFlow.Shared.Security;

namespace PortFlow.Identity.Web;

[ApiController]
[Route("api/v1/me")]
public sealed class MeController(
    ICurrentUserProvider users,
    IdentityQueries queries,
    DelegationService delegations,
    IUserDirectory directory) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<MeResponse> Me()
    {
        var ct = HttpContext.RequestAborted;
        var user = (TokenCurrentUser)users.Current();
        var received = await delegations.ActiveForAsync(user.Id, ct);
        var names = await directory.FullNamesAsync(received.Select(d => d.FromUserId), ct);
        var actingFor = received
            .Select(d => new ActingFor(d.FromUserId, names.GetValueOrDefault(d.FromUserId), d.DocumentTypes, d.ValidTo))
            .ToList();
        return new MeResponse(user.Id, user.Username, user.FullName, await queries.DepartmentAsync(user.DepartmentId, ct),
            user.Roles, user.DataScope, user.Permissions, actingFor);
    }

    public sealed record MeResponse(Guid Id, string Username, string FullName, DepartmentRef? Department,
        IReadOnlySet<string> Roles, DataScope DataScope, IReadOnlySet<string> Permissions,
        IReadOnlyList<ActingFor> ActingFor);

    public sealed record ActingFor(Guid UserId, string? FullName, IReadOnlyCollection<string> DocumentTypes,
        LocalDate ValidTo);
}
