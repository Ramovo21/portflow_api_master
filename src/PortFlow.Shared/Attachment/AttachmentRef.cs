using NodaTime;

namespace PortFlow.Shared.Attachment;

public sealed record AttachmentRef(Guid Id, string EntityType, Guid EntityId, string FileName, string ContentType,
    string Sha256, long Size, Guid UploadedBy, Instant UploadedAt, bool Locked);
