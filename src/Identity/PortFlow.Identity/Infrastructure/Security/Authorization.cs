using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using PortFlow.Identity.Api;
using PortFlow.Identity.Application;
using PortFlow.Identity.Domain;
using PortFlow.Shared.Security;
using PortFlow.Shared.Web;

namespace PortFlow.Identity.Infrastructure.Security;

/// <summary>Tầng 1: user phải có permission (claim <c>permission</c> lấy từ role trong DB).</summary>
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

internal sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasClaim(PortflowClaims.Permission, requirement.Permission))
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}

/// <summary>Dựng policy <c>perm:&lt;code&gt;</c> cho <see cref="RequirePermissionAttribute"/> theo yêu cầu.</summary>
internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName);
        }
        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName[RequirePermissionAttribute.PolicyPrefix.Length..]))
            .Build();
    }
}

/// <summary>
/// = RestAccessDeniedHandler: 403 dạng ProblemDetail. User vi phạm SoD bị chặn ở <c>Workflow.Act</c> nhận
/// <c>AUTH_SOD_VIOLATION</c> để biết lý do; còn lại <c>AUTH_FORBIDDEN</c>. Chưa xác thực → challenge (401).
/// </summary>
internal sealed class ProblemAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            var sodBlocked = context.Items[typeof(TokenCurrentUser)] is TokenCurrentUser { SodViolation: true }
                             && authorizeResult.AuthorizationFailure?.FailedRequirements.OfType<PermissionRequirement>()
                                 .Any(r => r.Permission == Permission.WORKFLOW_ACT) == true;
            await Problems.WriteAsync(context, sodBlocked
                ? Problems.Of(AuthErrorCode.AUTH_SOD_VIOLATION,
                    "The account is both an Administrator and holds an approval role (SoD violation), so it cannot approve")
                : Problems.Of(AuthErrorCode.AUTH_FORBIDDEN, "You do not have permission to perform this action"));
            return;
        }
        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}

/// <summary>Tầng 2: kiểm <see cref="RequireScopeAttribute"/> sau khi đã qua permission (middleware authorization).</summary>
internal sealed class ScopeAuthorizationFilter(ScopedPermissionEvaluator evaluator, ICurrentUserProvider users)
    : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        foreach (var scope in context.ActionDescriptor.EndpointMetadata.OfType<RequireScopeAttribute>())
        {
            var id = context.RouteData.Values.GetValueOrDefault(scope.RouteParameter);
            if (!await evaluator.HasPermissionAsync(users.Current(), id, scope.ResourceType,
                    context.HttpContext.RequestAborted))
            {
                var problem = Problems.Of(AuthErrorCode.AUTH_FORBIDDEN, "You do not have permission to perform this action");
                Problems.Enrich(context.HttpContext, problem);
                context.Result = new ObjectResult(problem)
                {
                    StatusCode = StatusCodes.Status403Forbidden,
                    ContentTypes = { Problems.ContentType },
                };
                return;
            }
        }
    }
}

/// <summary>= SecurityContextCurrentUserProvider: user của request; ngoài HTTP (consumer, job) là SystemUser.</summary>
internal sealed class HttpContextCurrentUserProvider(IHttpContextAccessor http) : ICurrentUserProvider
{
    public ICurrentUser Current() =>
        http.HttpContext?.Items[typeof(TokenCurrentUser)] as TokenCurrentUser as ICurrentUser ?? SystemUser.Instance;
}

/// <summary>Bộ lọc phòng ban cho truy vấn danh sách theo phạm vi dữ liệu của role.</summary>
internal sealed class RoleDataScopeQuery(IRolePermissions roles) : IDataScopeQuery
{
    public async Task<Guid?> DepartmentFilterAsync(ICurrentUser user, CancellationToken ct = default)
    {
        if (user.IsSystem || await roles.DataScopeOfAsync(user.Roles, ct) == DataScope.ALL)
        {
            return null;
        }
        return user.DepartmentId ?? Guid.Empty;
    }
}
