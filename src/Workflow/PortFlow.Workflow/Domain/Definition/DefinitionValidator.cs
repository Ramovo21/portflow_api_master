using System.Text.RegularExpressions;
using PortFlow.Shared.Domain;

namespace PortFlow.Workflow.Domain.Definition;

/// <summary>
/// Kiểm tra định nghĩa quy trình (V1–V8). Lỗi chặn lưu/kích hoạt; cảnh báo chỉ hiển thị.
/// V7 (chưa module nào xử lý loại hồ sơ) là cảnh báo khi lưu, lỗi khi kích hoạt.
/// </summary>
public sealed partial class DefinitionValidator(
    IConditionEvaluator conditions,
    IAssigneeDirectory directory,
    Func<string, bool> handlerRegistered)
{
    internal const int MaxSteps = 15;
    internal const int MaxName = 100;
    internal const int MaxCondition = 500;
    internal const int MaxSlaHours = 720;
    internal const string ActPermission = "Workflow.Act";

    internal static readonly IReadOnlyDictionary<string, object?> SampleContext = new Dictionary<string, object?>
    {
        ["docType"] = "SAMPLE",
        ["amount"] = 0m,
        ["currency"] = "VND",
        ["customerTier"] = "A",
        ["department"] = "SAMPLE",
        ["isAdjustment"] = false,
        ["addendumType"] = "SAMPLE",
        ["scope"] = "SAMPLE",
        ["serviceType"] = "SAMPLE",
        ["maxDeltaPct"] = 0m,
    };

    public async Task<IReadOnlyList<Violation>> ValidateAsync(DefinitionContent content, bool forActivation,
        CancellationToken ct = default)
    {
        var violations = new List<Violation>();
        var type = content.DocumentType;
        if (type is null || !DocumentTypePattern().IsMatch(type))
        {
            violations.Add(Violation.Error("V1", "documentType", "documentType must match ^[A-Z][A-Z_]{1,49}$"));
        }
        else if (!handlerRegistered(type))
        {
            var message = "No module has registered to handle document type " + type;
            violations.Add(forActivation
                ? Violation.Error("V7", "documentType", message)
                : Violation.Warning("V7", "documentType", message));
        }
        var steps = content.StepList;
        ValidateStepList(steps, violations);
        for (var i = 0; i < steps.Count; i++)
        {
            await ValidateStepAsync(steps[i], $"steps[{i}]", violations, ct);
        }
        if (steps.Count > 0 && steps.All(step => step.Conditional))
        {
            violations.Add(Violation.Error("V2", "steps", "At least one step without a condition is required"));
        }
        if (steps.Count > 0 && steps[^1].OnReject == OnReject.BACK_TO_SUBMITTER)
        {
            violations.Add(Violation.Warning("V8", $"steps[{steps.Count - 1}].onReject",
                "The last step should be TERMINATE"));
        }
        return violations;
    }

    private static void ValidateStepList(IReadOnlyList<StepDefinition> steps, List<Violation> violations)
    {
        if (steps.Count == 0 || steps.Count > MaxSteps)
        {
            violations.Add(Violation.Error("V1", "steps", $"The number of steps must be between 1 and {MaxSteps}"));
            return;
        }
        var seen = new HashSet<int>();
        for (var i = 0; i < steps.Count; i++)
        {
            var order = steps[i].Order;
            if (!seen.Add(order))
            {
                violations.Add(Violation.Error("V1", $"steps[{i}].order", $"Duplicate step order: {order}"));
            }
            else if (order != i + 1)
            {
                violations.Add(Violation.Error("V1", $"steps[{i}].order",
                    $"Step order must be consecutive from 1 (expected {i + 1}, got {order})"));
            }
        }
    }

    private async Task ValidateStepAsync(StepDefinition step, string path, List<Violation> violations,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(step.Name) || step.Name.Length > MaxName)
        {
            violations.Add(Violation.Error("V1", path + ".name", $"Step name is required, maximum {MaxName} characters"));
        }
        if (step.OnReject is null)
        {
            violations.Add(Violation.Error("V1", path + ".onReject", "Missing onReject (TERMINATE | BACK_TO_SUBMITTER)"));
        }
        if (step.SlaHours is { } sla && (sla < 1 || sla > MaxSlaHours))
        {
            violations.Add(Violation.Error("V1", path + ".slaHours", $"slaHours must be between 1 and {MaxSlaHours}"));
        }
        await ValidateAssigneeAsync(step, path + ".assignee", violations, ct);
        ValidateCondition(step.Condition, path + ".condition", violations);
    }

    private async Task ValidateAssigneeAsync(StepDefinition step, string path, List<Violation> violations,
        CancellationToken ct)
    {
        var rule = step.Assignee;
        if (rule?.Type is not { } type)
        {
            violations.Add(Violation.Error("V3", path, "Missing assignee.type"));
            return;
        }
        switch (type)
        {
            case AssigneeType.ROLE:
                await ValidateRoleAsync(rule.Value, path + ".value", violations, ct);
                break;
            case AssigneeType.USER:
                await ValidateUserAsync(rule.Value, path + ".value", violations, ct);
                break;
            case AssigneeType.DYNAMIC:
                if (rule.Value is null || !AssigneeRule.DynamicExpressions.Contains(rule.Value))
                {
                    violations.Add(Violation.Error("V3", path + ".value",
                        "DYNAMIC only accepts [" + string.Join(", ", AssigneeRule.DynamicExpressions) + "]"));
                }
                break;
            case AssigneeType.GROUP:
                var userIds = rule.UserIds ?? [];
                if (userIds.Count > 0)
                {
                    for (var i = 0; i < userIds.Count; i++)
                    {
                        if (!await directory.IsActiveUserAsync(userIds[i], ct))
                        {
                            violations.Add(Violation.Error("V3", $"{path}.userIds[{i}]", "User does not exist or is inactive"));
                        }
                    }
                }
                else
                {
                    await ValidateRoleAsync(rule.Value, path + ".value", violations, ct);
                }
                break;
        }
        if (step.RequiresAll && type != AssigneeType.GROUP && type != AssigneeType.ROLE)
        {
            violations.Add(Violation.Error("V5", path, "requiresAllApprovers is only allowed with GROUP or ROLE"));
        }
    }

    private async Task ValidateRoleAsync(string? roleCode, string path, List<Violation> violations, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(roleCode))
        {
            violations.Add(Violation.Error("V3", path, "Missing role code (or userIds for GROUP)"));
            return;
        }
        var permissions = await directory.PermissionsOfRoleAsync(roleCode, ct);
        if (permissions is null)
        {
            violations.Add(Violation.Error("V3", path, "Role does not exist: " + roleCode));
        }
        else if (!permissions.Contains(ActPermission))
        {
            violations.Add(Violation.Error("V6", path, $"Role {roleCode} does not have permission {ActPermission}"));
        }
    }

    private async Task ValidateUserAsync(string? value, string path, List<Violation> violations, CancellationToken ct)
    {
        if (value is not null && !Guid.TryParse(value, out _))
        {
            violations.Add(Violation.Error("V3", path, "USER requires value to be a user UUID"));
            return;
        }
        if (value is null || !await directory.IsActiveUserAsync(Guid.Parse(value), ct))
        {
            violations.Add(Violation.Error("V3", path, "User does not exist or is inactive"));
        }
    }

    private void ValidateCondition(string? condition, string path, List<Violation> violations)
    {
        if (condition is null)
        {
            return;
        }
        if (condition.Length > MaxCondition)
        {
            violations.Add(Violation.Error("V4", path, $"Condition maximum {MaxCondition} characters"));
            return;
        }
        try
        {
            conditions.Evaluate(condition, SampleContext);
        }
        catch (DomainException ex)
        {
            var cause = ex.Args.GetValueOrDefault("cause");
            violations.Add(Violation.Error("V4", path,
                "Condition is not valid (only variables [" + string.Join(", ", IConditionEvaluator.Variables)
                + "], must return true/false)" + (cause is not null ? ": " + cause : "")));
        }
    }

    [GeneratedRegex("^[A-Z][A-Z_]{1,49}$")]
    private static partial Regex DocumentTypePattern();
}
