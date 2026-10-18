using System.Text.Json.Serialization;

namespace PortFlow.Workflow.Domain.Definition;

public enum AssigneeType
{
    ROLE,
    USER,
    DYNAMIC,
    GROUP,
}

public enum AssigneeScope
{
    ANY,
    SAME_DEPARTMENT_AS_SUBMITTER,
}

public enum OnReject
{
    TERMINATE,
    BACK_TO_SUBMITTER,
}

public enum ResubmitPolicy
{
    RESTART,
    RESUME_AT_REQUESTER,
}

public enum DefinitionStatus
{
    DRAFT,
    ACTIVE,
    RETIRED,
}

/// <summary>Ai duyệt một bước: theo role (toàn công ty hoặc cùng phòng người gửi), user cụ thể, động, hoặc nhóm.</summary>
public sealed record AssigneeRule
{
    public const string DYNAMIC_DEPARTMENT_HEAD = "submitter.department.head";
    public const string DYNAMIC_MANAGER = "submitter.manager";

    public static readonly IReadOnlySet<string> DynamicExpressions =
        new HashSet<string> { DYNAMIC_DEPARTMENT_HEAD, DYNAMIC_MANAGER };

    [JsonConstructor]
    public AssigneeRule(AssigneeType? type, string? value, AssigneeScope? scope, IReadOnlyList<Guid>? userIds)
    {
        Type = type;
        Value = value;
        Scope = scope ?? AssigneeScope.ANY;
        UserIds = userIds?.ToList() ?? [];
    }

    public AssigneeType? Type { get; }

    public string? Value { get; }

    public AssigneeScope? Scope { get; }

    public IReadOnlyList<Guid>? UserIds { get; }

    public static AssigneeRule Role(string roleCode) => new(AssigneeType.ROLE, roleCode, AssigneeScope.ANY, []);

    public string Hint() => Type switch
    {
        AssigneeType.ROLE => "Chức danh " + Value + (Scope == AssigneeScope.SAME_DEPARTMENT_AS_SUBMITTER ? " (cùng phòng)" : ""),
        AssigneeType.USER => "Người duyệt chỉ định",
        AssigneeType.DYNAMIC => Value == DYNAMIC_DEPARTMENT_HEAD ? "Trưởng phòng của người gửi" : "Quản lý của người gửi",
        AssigneeType.GROUP => "Nhóm " + (Value ?? $"{UserIds?.Count ?? 0} người"),
        _ => "",
    };
}

public sealed record StepDefinition
{
    [JsonConstructor]
    public StepDefinition(int order, string? name, AssigneeRule? assignee, OnReject? onReject, string? condition,
        int? slaHours, bool? requiresAllApprovers)
    {
        Order = order;
        Name = name?.Trim();
        Assignee = assignee;
        OnReject = onReject;
        Condition = string.IsNullOrWhiteSpace(condition) ? null : condition.Trim();
        SlaHours = slaHours;
        RequiresAllApprovers = requiresAllApprovers == true;
    }

    public int Order { get; }

    public string? Name { get; }

    public AssigneeRule? Assignee { get; }

    public OnReject? OnReject { get; }

    public string? Condition { get; }

    public int? SlaHours { get; }

    public bool? RequiresAllApprovers { get; }

    [JsonIgnore]
    public bool RequiresAll => RequiresAllApprovers == true;

    [JsonIgnore]
    public bool Conditional => Condition is not null;
}

public sealed record FinalApproveAction
{
    public static readonly FinalApproveAction None = new(false);

    [JsonConstructor]
    public FinalApproveAction(bool? emitESign)
    {
        EmitESign = emitESign == true;
    }

    public bool? EmitESign { get; }
}

/// <summary>Nội dung JSON của một version định nghĩa quy trình; các bước luôn sắp theo <c>order</c>.</summary>
public sealed record DefinitionContent
{
    [JsonConstructor]
    public DefinitionContent(string? documentType, ResubmitPolicy? resubmitPolicy, IReadOnlyList<StepDefinition>? steps,
        FinalApproveAction? onFinalApprove)
    {
        DocumentType = documentType;
        ResubmitPolicy = resubmitPolicy ?? Definition.ResubmitPolicy.RESTART;
        Steps = (steps ?? []).OrderBy(step => step.Order).ToList();
        OnFinalApprove = onFinalApprove ?? FinalApproveAction.None;
    }

    public string? DocumentType { get; }

    public ResubmitPolicy? ResubmitPolicy { get; }

    public IReadOnlyList<StepDefinition>? Steps { get; }

    public FinalApproveAction? OnFinalApprove { get; }

    [JsonIgnore]
    public IReadOnlyList<StepDefinition> StepList => Steps ?? [];

    [JsonIgnore]
    public ResubmitPolicy Policy => ResubmitPolicy ?? Definition.ResubmitPolicy.RESTART;

    [JsonIgnore]
    public bool EmitESign => OnFinalApprove?.EmitESign == true;

    public DefinitionContent WithDocumentType(string? type) => new(type, ResubmitPolicy, Steps, OnFinalApprove);
}

public sealed record Violation(string Rule, string? Path, string Message, Violation.Level Severity)
{
    public enum Level
    {
        ERROR,
        WARNING,
    }

    public static Violation Error(string rule, string path, string message) => new(rule, path, message, Level.ERROR);

    public static Violation Warning(string rule, string path, string message) => new(rule, path, message, Level.WARNING);

    [JsonIgnore]
    public bool IsError => Severity == Level.ERROR;

    public Dictionary<string, object?> ToProblemError() => new()
    {
        ["field"] = Path,
        ["code"] = Rule,
        ["message"] = Message,
    };
}
