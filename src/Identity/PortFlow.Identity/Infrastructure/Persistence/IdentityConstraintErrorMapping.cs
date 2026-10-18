using PortFlow.Identity.Domain;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Persistence;

namespace PortFlow.Identity.Infrastructure.Persistence;

internal sealed class IdentityConstraintErrorMapping : IConstraintErrorMapping
{
    public IReadOnlyDictionary<string, IErrorCode> Mappings { get; } = new Dictionary<string, IErrorCode>
    {
        ["delegation_no_overlap"] = AuthErrorCode.AUTH_DELEGATION_OVERLAP,
        ["ck_delegation_not_self"] = SysErrorCode.SYS_VALIDATION,
        ["ck_delegation_range"] = SysErrorCode.SYS_VALIDATION,
        ["ux_department_code"] = AuthErrorCode.AUTH_DEPARTMENT_CODE_EXISTS,
    };
}
