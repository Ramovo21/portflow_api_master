using PortFlow.Identity.Domain;
using PortFlow.Shared.Security;

namespace PortFlow.Identity.Application;

/// <summary>Người dùng của request hiện tại, dựng từ JWT + bản sao trong DB (permission luôn lấy từ DB).</summary>
public sealed record TokenCurrentUser(
    Guid Id,
    string Username,
    string FullName,
    Guid? DepartmentId,
    IReadOnlySet<string> Roles,
    IReadOnlySet<string> Permissions,
    DataScope DataScope,
    bool SodViolation) : ICurrentUser;
