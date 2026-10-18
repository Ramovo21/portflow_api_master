using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PortFlow.Shared.Security;
using PortFlow.Shared.Web;
using PortFlow.Workflow.Application;

namespace PortFlow.Workflow.Web;

[ApiController]
[Route("api/v1/inbox")]
public sealed class InboxController(InboxQueryService inbox, ICurrentUserProvider users) : ControllerBase
{
    private const int MaxPageSize = 100;

    [HttpGet]
    [Authorize]
    public Task<PageResponse<InboxItem>> List([FromQuery] InboxTab tab = InboxTab.ASSIGNED,
        [FromQuery] string? docType = null, [FromQuery] bool overdue = false, [FromQuery] int page = 0,
        [FromQuery] int size = 20) =>
        inbox.InboxAsync(users.Current(), tab, docType, overdue, Math.Max(page, 0), Math.Clamp(size, 1, MaxPageSize),
            HttpContext.RequestAborted);

    [HttpGet("counters")]
    [Authorize]
    public Task<InboxCounters> Counters() => inbox.CountersAsync(users.Current(), HttpContext.RequestAborted);
}
