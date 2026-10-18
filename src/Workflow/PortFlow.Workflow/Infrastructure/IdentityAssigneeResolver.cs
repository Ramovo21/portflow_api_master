using PortFlow.Identity.Api;
using PortFlow.Workflow.Domain;
using PortFlow.Workflow.Domain.Definition;

namespace PortFlow.Workflow.Infrastructure;

/// <summary>Phân giải người duyệt từ bản sao user của identity (user đã đồng bộ nhưng chưa đăng nhập vẫn được giao).</summary>
internal sealed class IdentityAssigneeResolver(IUserDirectory users) : IAssigneeResolver, IAssigneeDirectory
{
    public async Task<IReadOnlyList<Guid>> ResolveAsync(AssigneeRule rule, ResolveContext context, CancellationToken ct)
    {
        switch (rule.Type)
        {
            case AssigneeType.ROLE:
                return await ByRoleAsync(rule.Value, rule.Scope, context, ct);
            case AssigneeType.USER:
                return Guid.TryParse(rule.Value, out var userId) && await users.IsActiveAsync(userId, ct) ? [userId] : [];
            case AssigneeType.DYNAMIC:
                var head = rule.Value switch
                {
                    AssigneeRule.DYNAMIC_DEPARTMENT_HEAD => context.SubmitterDepartmentId is { } department
                        ? await users.HeadOfDepartmentAsync(department, ct)
                        : null,
                    AssigneeRule.DYNAMIC_MANAGER => context.SubmitterId is { } submitter
                        ? await users.HeadOfDepartmentOfAsync(submitter, ct)
                        : null,
                    _ => null,
                };
                return head is null ? [] : [head.Id];
            case AssigneeType.GROUP:
                if (rule.UserIds is not { Count: > 0 } members)
                {
                    return await ByRoleAsync(rule.Value, rule.Scope, context, ct);
                }
                var active = new List<Guid>();
                foreach (var member in members)
                {
                    if (await users.IsActiveAsync(member, ct))
                    {
                        active.Add(member);
                    }
                }
                return active;
            default:
                return [];
        }
    }

    private async Task<IReadOnlyList<Guid>> ByRoleAsync(string? roleCode, AssigneeScope? scope, ResolveContext context,
        CancellationToken ct)
    {
        if (roleCode is null)
        {
            return [];
        }
        if (scope == AssigneeScope.SAME_DEPARTMENT_AS_SUBMITTER)
        {
            return context.SubmitterDepartmentId is { } department
                ? (await users.FindActiveByRoleInDepartmentAsync(roleCode, department, ct)).Select(u => u.Id).ToList()
                : [];
        }
        return (await users.FindActiveByRoleAsync(roleCode, ct)).Select(u => u.Id).ToList();
    }

    public Task<IReadOnlySet<string>?> PermissionsOfRoleAsync(string roleCode, CancellationToken ct) =>
        users.PermissionsOfRoleAsync(roleCode, ct);

    public Task<bool> IsActiveUserAsync(Guid userId, CancellationToken ct) => users.IsActiveAsync(userId, ct);
}
