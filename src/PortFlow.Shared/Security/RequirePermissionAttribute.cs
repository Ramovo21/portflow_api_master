using Microsoft.AspNetCore.Authorization;

namespace PortFlow.Shared.Security;

/// <summary>
/// Tầng 1 – permission (= <c>@PreAuthorize("hasAuthority('X')")</c>). Nhiều attribute trên cùng action = AND.
/// Policy <c>perm:&lt;code&gt;</c> do module identity cung cấp.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "perm:";

    public RequirePermissionAttribute(string permission)
        : base(PolicyPrefix + permission)
    {
        Permission = permission;
    }

    public string Permission { get; }
}
