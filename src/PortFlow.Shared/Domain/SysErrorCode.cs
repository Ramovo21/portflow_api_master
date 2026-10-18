using System.Net;

namespace PortFlow.Shared.Domain;

public static class SysErrorCode
{
    public static readonly ErrorCode SYS_VALIDATION =
        new(nameof(SYS_VALIDATION), HttpStatusCode.UnprocessableContent, "Invalid data");

    public static readonly ErrorCode SYS_BAD_REQUEST =
        new(nameof(SYS_BAD_REQUEST), HttpStatusCode.BadRequest, "Invalid request");

    public static readonly ErrorCode SYS_UNAUTHORIZED =
        new(nameof(SYS_UNAUTHORIZED), HttpStatusCode.Unauthorized, "Not authenticated");

    public static readonly ErrorCode SYS_FORBIDDEN =
        new(nameof(SYS_FORBIDDEN), HttpStatusCode.Forbidden, "Access denied");

    public static readonly ErrorCode SYS_NOT_FOUND =
        new(nameof(SYS_NOT_FOUND), HttpStatusCode.NotFound, "Not found");

    public static readonly ErrorCode SYS_METHOD_NOT_ALLOWED =
        new(nameof(SYS_METHOD_NOT_ALLOWED), HttpStatusCode.MethodNotAllowed, "Method not allowed");

    public static readonly ErrorCode SYS_CONCURRENT_MODIFICATION =
        new(nameof(SYS_CONCURRENT_MODIFICATION), HttpStatusCode.Conflict, "Data was modified by another user");

    public static readonly ErrorCode SYS_IDEMPOTENCY_KEY_REQUIRED =
        new(nameof(SYS_IDEMPOTENCY_KEY_REQUIRED), HttpStatusCode.BadRequest, "Missing Idempotency-Key header");

    public static readonly ErrorCode SYS_IDEMPOTENCY_KEY_REUSED =
        new(nameof(SYS_IDEMPOTENCY_KEY_REUSED), HttpStatusCode.UnprocessableContent,
            "Idempotency-Key was already used for a different request");

    public static readonly ErrorCode SYS_IDEMPOTENCY_IN_PROGRESS =
        new(nameof(SYS_IDEMPOTENCY_IN_PROGRESS), HttpStatusCode.Conflict,
            "A request with the same Idempotency-Key is already being processed");

    public static readonly ErrorCode SYS_CURRENCY_MISMATCH =
        new(nameof(SYS_CURRENCY_MISMATCH), HttpStatusCode.UnprocessableContent,
            "Cannot calculate between two different currencies");

    public static readonly ErrorCode SYS_FILE_TOO_LARGE =
        new(nameof(SYS_FILE_TOO_LARGE), HttpStatusCode.RequestEntityTooLarge, "File too large");

    public static readonly ErrorCode SYS_FILE_TYPE_NOT_ALLOWED =
        new(nameof(SYS_FILE_TYPE_NOT_ALLOWED), HttpStatusCode.UnsupportedMediaType, "File type not allowed");

    public static readonly ErrorCode SYS_UNSUPPORTED_MEDIA_TYPE =
        new(nameof(SYS_UNSUPPORTED_MEDIA_TYPE), HttpStatusCode.UnsupportedMediaType, "Unsupported content type");

    public static readonly ErrorCode SYS_ATTACHMENT_ENTITY_UNSUPPORTED =
        new(nameof(SYS_ATTACHMENT_ENTITY_UNSUPPORTED), HttpStatusCode.UnprocessableContent,
            "Record type does not support attachments");

    public static readonly ErrorCode SYS_DATA_INTEGRITY =
        new(nameof(SYS_DATA_INTEGRITY), HttpStatusCode.Conflict, "Data integrity violation");

    public static readonly ErrorCode SYS_INTERNAL =
        new(nameof(SYS_INTERNAL), HttpStatusCode.InternalServerError, "System error");
}
