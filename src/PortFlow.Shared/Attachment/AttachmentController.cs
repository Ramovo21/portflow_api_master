using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NodaTime;
using PortFlow.Shared.Config;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Idempotency;

namespace PortFlow.Shared.Attachment;

[ApiController]
[Route("api/v1/attachments")]
public sealed class AttachmentController(DefaultAttachmentService attachments, IOptions<PortFlowOptions> options)
    : ControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    [Idempotent]
    [Authorize]
    public async Task<ActionResult<AttachmentRef>> Upload([FromForm] string entityType, [FromForm] Guid entityId,
        IFormFile file)
    {
        var created = await attachments.UploadAsync(entityType, entityId, file, HttpContext.RequestAborted);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpGet("{id:guid}/download-url")]
    [Authorize]
    public async Task<DownloadUrlResponse> DownloadUrl(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var attachment = await attachments.FindAsync(id, ct)
                         ?? throw new DomainException(SysErrorCode.SYS_NOT_FOUND, "Attachment not found");
        await attachments.PolicyFor(attachment.EntityType).CheckCanViewAsync(attachment.EntityId, ct);
        var ttl = options.Value.Storage.PresignTtl;
        var url = await attachments.PresignedDownloadAsync(id, ttl, ct);
        return new DownloadUrlResponse(url, SystemClock.Instance.GetCurrentInstant() + Duration.FromTimeSpan(ttl));
    }

    public sealed record DownloadUrlResponse(Uri Url, Instant ExpiresAt);
}
