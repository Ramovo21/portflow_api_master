using Dapper;
using Microsoft.AspNetCore.Http;
using NodaTime;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Id;
using PortFlow.Shared.Persistence;
using PortFlow.Shared.Security;
using PortFlow.Shared.Time;

namespace PortFlow.Shared.Attachment;

public sealed class DefaultAttachmentService : IAttachmentService
{
    internal const long MaxSize = 25L * 1024 * 1024;

    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        ["application/pdf"] = ["pdf"],
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ["docx"],
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = ["xlsx"],
        ["image/png"] = ["png"],
        ["image/jpeg"] = ["jpg", "jpeg"],
    };

    private const string Columns = """
        id AS Id, entity_type AS EntityType, entity_id AS EntityId, file_name AS FileName,
        content_type AS ContentType, sha256 AS Sha256, size AS Size, uploaded_by AS UploadedBy,
        uploaded_at AS UploadedAt, locked AS Locked
        """;

    private readonly DbSession _session;
    private readonly ITransactionRunner _tx;
    private readonly IObjectStorage _storage;
    private readonly ICurrentUserProvider _users;
    private readonly IBusinessClock _clock;
    private readonly Dictionary<string, IAttachmentAccessPolicy> _policies;

    internal DefaultAttachmentService(DbSession session, ITransactionRunner tx, IObjectStorage storage,
        ICurrentUserProvider users, IBusinessClock clock, IEnumerable<IAttachmentAccessPolicy> policies)
    {
        _session = session;
        _tx = tx;
        _storage = storage;
        _users = users;
        _clock = clock;
        _policies = policies.ToDictionary(p => p.EntityType);
    }

    public Task<AttachmentRef> UploadAsync(string entityType, Guid entityId, IFormFile file, CancellationToken ct = default) =>
        _tx.ExecuteAsync(async token =>
        {
            await PolicyFor(entityType).CheckCanUploadAsync(entityId, token);
            if (file.Length == 0)
            {
                throw new DomainException(SysErrorCode.SYS_VALIDATION, "File is empty");
            }
            if (file.Length > MaxSize)
            {
                throw new DomainException(SysErrorCode.SYS_FILE_TOO_LARGE, "File exceeds 25 MB");
            }
            var contentType = RequireAllowedType(file);

            var id = UuidV7.Next();
            var now = _clock.Now();
            var storageKey = StorageKey(entityType, id, now);
            var sha256 = await StoreAsync(storageKey, file, contentType, token);
            _session.AfterCompletion(committed => committed ? Task.CompletedTask : _storage.DeleteAsync(storageKey));

            var uploader = _users.Current().Id;
            var fileName = SafeFileName(file.FileName);
            var connection = await _session.OpenAsync(token);
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO shared.attachment
                  (id, entity_type, entity_id, file_name, content_type, storage_key, sha256, size, uploaded_by, uploaded_at)
                VALUES (@id, @entityType, @entityId, @fileName, @contentType, @storageKey, @sha256, @size, @uploader, @now)
                """, new { id, entityType, entityId, fileName, contentType, storageKey, sha256, size = file.Length, uploader, now },
                _session.Transaction, cancellationToken: token));
            return new AttachmentRef(id, entityType, entityId, fileName, contentType, sha256, file.Length, uploader, now,
                false);
        }, ct);

    public async Task<AttachmentRef?> FindAsync(Guid attachmentId, CancellationToken ct = default)
    {
        var connection = await _session.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<AttachmentRef>(new CommandDefinition(
            $"SELECT {Columns} FROM shared.attachment WHERE id = @attachmentId",
            new { attachmentId }, _session.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AttachmentRef>> ListAsync(string entityType, Guid entityId, CancellationToken ct = default)
    {
        var connection = await _session.OpenAsync(ct);
        var rows = await connection.QueryAsync<AttachmentRef>(new CommandDefinition($"""
            SELECT {Columns} FROM shared.attachment WHERE entity_type = @entityType AND entity_id = @entityId
            ORDER BY uploaded_at
            """, new { entityType, entityId }, _session.Transaction, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Uri> PresignedDownloadAsync(Guid attachmentId, TimeSpan ttl, CancellationToken ct = default)
    {
        var connection = await _session.OpenAsync(ct);
        var key = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                      "SELECT storage_key FROM shared.attachment WHERE id = @attachmentId",
                      new { attachmentId }, _session.Transaction, cancellationToken: ct))
                  ?? throw new DomainException(SysErrorCode.SYS_NOT_FOUND, "Attachment not found");
        return await _storage.PresignedGetAsync(key, ttl);
    }

    public Task LockAllAsync(string entityType, Guid entityId, CancellationToken ct = default) =>
        _tx.ExecuteAsync(async token =>
        {
            var connection = await _session.OpenAsync(token);
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE shared.attachment SET locked = true WHERE entity_type = @entityType AND entity_id = @entityId",
                new { entityType, entityId }, _session.Transaction, cancellationToken: token));
        }, ct);

    internal IAttachmentAccessPolicy PolicyFor(string entityType) =>
        _policies.GetValueOrDefault(entityType)
        ?? throw new DomainException(SysErrorCode.SYS_ATTACHMENT_ENTITY_UNSUPPORTED,
            $"Record type '{entityType}' does not support attachments");

    private async Task<string> StoreAsync(string storageKey, IFormFile file, string contentType, CancellationToken ct)
    {
        await using var hashing = new HashingReadStream(file.OpenReadStream());
        await _storage.PutAsync(storageKey, hashing, file.Length, contentType, ct);
        return hashing.HexDigest();
    }

    private static string RequireAllowedType(IFormFile file)
    {
        var contentType = (file.ContentType ?? "").ToLowerInvariant();
        var name = (file.FileName ?? "").ToLowerInvariant();
        var extension = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : "";
        if (!Allowed.TryGetValue(contentType, out var extensions) || !extensions.Contains(extension))
        {
            throw new DomainException(SysErrorCode.SYS_FILE_TYPE_NOT_ALLOWED, "Only PDF, DOCX, XLSX, PNG, JPG are accepted");
        }
        return contentType;
    }

    private static string StorageKey(string entityType, Guid id, Instant now)
    {
        var date = now.InUtc();
        return $"{entityType.ToLowerInvariant()}/{date.Year}/{date.Month:00}/{id}";
    }

    private static string SafeFileName(string? original)
    {
        if (string.IsNullOrWhiteSpace(original))
        {
            return "file";
        }
        var name = original.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        return name.Length > 255 ? name[^255..] : name;
    }
}
