using Microsoft.AspNetCore.Mvc;
using PortFlow.Shared.Idempotency;
using PortFlow.Shared.Security;

namespace PortFlow.Shared.Outbox;

/// <summary>Truy vấn + thao tác quản trị outbox (= OutboxQueries + OutboxMaintenanceJob.resubmitOlderThan).</summary>
public sealed class OutboxAdministration
{
    private readonly EventPublicationRepository _repository;
    private readonly IncompleteEventPublications _incomplete;

    internal OutboxAdministration(EventPublicationRepository repository, IncompleteEventPublications incomplete)
    {
        _repository = repository;
        _incomplete = incomplete;
    }

    public Task<long> CountIncompleteAsync(CancellationToken ct) => _repository.CountIncompleteAsync(ct);

    public Task<IReadOnlyList<IncompletePublication>> IncompleteAsync(int limit, CancellationToken ct) =>
        _repository.IncompleteAsync(limit, ct);

    public Task ResubmitOlderThanAsync(TimeSpan age, CancellationToken ct) => _incomplete.ResubmitOlderThanAsync(age, ct);
}

[ApiController]
[Route("api/v1/admin/outbox")]
public sealed class OutboxAdminController(OutboxAdministration outbox) : ControllerBase
{
    // shared không phụ thuộc identity.api → dùng chuỗi như @PreAuthorize("hasAuthority('System.Admin')")
    private const string SystemAdmin = "System.Admin";

    [HttpGet]
    [RequirePermission(SystemAdmin)]
    public Task<IReadOnlyList<IncompletePublication>> Incomplete([FromQuery] int limit = 100) =>
        outbox.IncompleteAsync(Math.Clamp(limit, 1, 500), HttpContext.RequestAborted);

    [HttpPost("resubmit")]
    [Idempotent]
    [RequirePermission(SystemAdmin)]
    public async Task<IActionResult> Resubmit()
    {
        var before = await outbox.CountIncompleteAsync(HttpContext.RequestAborted);
        await outbox.ResubmitOlderThanAsync(TimeSpan.Zero, CancellationToken.None);
        return Accepted(new Dictionary<string, object> { ["incompleteBefore"] = before });
    }
}
