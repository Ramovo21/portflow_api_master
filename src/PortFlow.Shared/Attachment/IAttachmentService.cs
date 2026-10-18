using Microsoft.AspNetCore.Http;

namespace PortFlow.Shared.Attachment;

public interface IAttachmentService
{
    Task<AttachmentRef> UploadAsync(string entityType, Guid entityId, IFormFile file, CancellationToken ct = default);

    Task<AttachmentRef?> FindAsync(Guid attachmentId, CancellationToken ct = default);

    Task<IReadOnlyList<AttachmentRef>> ListAsync(string entityType, Guid entityId, CancellationToken ct = default);

    Task<Uri> PresignedDownloadAsync(Guid attachmentId, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Khóa mọi tệp của hồ sơ (gọi khi hồ sơ được duyệt/ký).</summary>
    Task LockAllAsync(string entityType, Guid entityId, CancellationToken ct = default);
}

/// <summary>
/// Module sở hữu hồ sơ khai báo quyền upload/xem tệp cho <see cref="EntityType"/> của mình.
/// Không có policy cho entityType → 422 SYS_ATTACHMENT_ENTITY_UNSUPPORTED.
/// </summary>
public interface IAttachmentAccessPolicy
{
    string EntityType { get; }

    Task CheckCanUploadAsync(Guid entityId, CancellationToken ct);

    Task CheckCanViewAsync(Guid entityId, CancellationToken ct);
}
