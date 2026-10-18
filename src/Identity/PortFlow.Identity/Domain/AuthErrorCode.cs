using System.Net;
using PortFlow.Shared.Domain;

namespace PortFlow.Identity.Domain;

public static class AuthErrorCode
{
    public static readonly ErrorCode AUTH_UNAUTHENTICATED =
        new(nameof(AUTH_UNAUTHENTICATED), HttpStatusCode.Unauthorized, "Not authenticated");

    public static readonly ErrorCode AUTH_INVALID_TOKEN =
        new(nameof(AUTH_INVALID_TOKEN), HttpStatusCode.Unauthorized, "Invalid token");

    public static readonly ErrorCode AUTH_TOKEN_EXPIRED =
        new(nameof(AUTH_TOKEN_EXPIRED), HttpStatusCode.Unauthorized, "Token has expired");

    public static readonly ErrorCode AUTH_USER_INACTIVE =
        new(nameof(AUTH_USER_INACTIVE), HttpStatusCode.Forbidden, "User has been deactivated");

    public static readonly ErrorCode AUTH_USER_NOT_PROVISIONED =
        new(nameof(AUTH_USER_NOT_PROVISIONED), HttpStatusCode.Forbidden, "User has not been assigned a department");

    public static readonly ErrorCode AUTH_FORBIDDEN =
        new(nameof(AUTH_FORBIDDEN), HttpStatusCode.Forbidden, "Not allowed to perform this action");

    public static readonly ErrorCode AUTH_SOD_VIOLATION =
        new(nameof(AUTH_SOD_VIOLATION), HttpStatusCode.Forbidden, "Separation-of-duties violation in approval");

    public static readonly ErrorCode AUTH_SYSTEM_ROLE_IMMUTABLE =
        new(nameof(AUTH_SYSTEM_ROLE_IMMUTABLE), HttpStatusCode.Conflict, "System role cannot be modified");

    public static readonly ErrorCode AUTH_DELEGATION_OVERLAP =
        new(nameof(AUTH_DELEGATION_OVERLAP), HttpStatusCode.Conflict, "Ủy quyền chồng thời gian");

    public static readonly ErrorCode AUTH_DEPARTMENT_CODE_EXISTS =
        new(nameof(AUTH_DEPARTMENT_CODE_EXISTS), HttpStatusCode.Conflict, "Department code already exists");

    public static readonly ErrorCode AUTH_IDP_UNAVAILABLE =
        new(nameof(AUTH_IDP_UNAVAILABLE), HttpStatusCode.ServiceUnavailable, "Keycloak is not responding");
}
