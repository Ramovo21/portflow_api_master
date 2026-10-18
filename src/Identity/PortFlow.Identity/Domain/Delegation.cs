using NodaTime;
using PortFlow.Shared.Domain;

namespace PortFlow.Identity.Domain;

/// <summary>
/// FR-W04: ủy quyền duyệt có thời hạn. Bất biến của chính ủy quyền nằm ở đây; điều kiện cần tra cứu bên ngoài
/// (user đang hoạt động, người nhận có quyền duyệt, loại hồ sơ tồn tại, không chồng thời gian) do service/DB kiểm.
/// <see cref="DocumentTypes"/> rỗng = mọi loại hồ sơ.
/// </summary>
public sealed class Delegation
{
    private Delegation()
    {
    }

    public Guid Id { get; private set; }

    public Guid FromUserId { get; private set; }

    public Guid ToUserId { get; private set; }

    public string[] DocumentTypes { get; private set; } = [];

    public LocalDate ValidFrom { get; private set; }

    public LocalDate ValidTo { get; private set; }

    public string Reason { get; private set; } = null!;

    public bool Revoked { get; private set; }

    public static Delegation Grant(Guid id, Guid fromUserId, Guid? toUserId, IEnumerable<string>? documentTypes,
        LocalDate? validFrom, LocalDate? validTo, string? reason, LocalDate today)
    {
        if (toUserId is not { } to || validFrom is not { } from || validTo is not { } until
            || string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(SysErrorCode.SYS_VALIDATION,
                "Missing delegate, validity period or reason for the delegation");
        }
        if (to == fromUserId)
        {
            throw new DomainException(SysErrorCode.SYS_VALIDATION, "Cannot delegate to yourself");
        }
        if (from > until)
        {
            throw new DomainException(SysErrorCode.SYS_VALIDATION, "Start date must be on or before the end date");
        }
        if (until < today)
        {
            throw new DomainException(SysErrorCode.SYS_VALIDATION, "The delegation period has already passed");
        }
        return new Delegation
        {
            Id = id,
            FromUserId = fromUserId,
            ToUserId = to,
            DocumentTypes = (documentTypes ?? []).Distinct().ToArray(),
            ValidFrom = from,
            ValidTo = until,
            Reason = reason.Trim(),
        };
    }

    public bool Covers(string documentType, LocalDate date) =>
        !Revoked && date >= ValidFrom && date <= ValidTo
        && (DocumentTypes.Length == 0 || DocumentTypes.Contains(documentType));

    /// <summary>Thu hồi; trả false nếu đã thu hồi trước đó (idempotent, không phát event lần hai).</summary>
    public bool Revoke()
    {
        if (Revoked)
        {
            return false;
        }
        Revoked = true;
        return true;
    }
}
