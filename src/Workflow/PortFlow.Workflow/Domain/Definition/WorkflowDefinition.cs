using NodaTime;
using PortFlow.Shared.Domain;

namespace PortFlow.Workflow.Domain.Definition;

/// <summary>Một version định nghĩa quy trình. Chỉ bản DRAFT sửa được; mỗi loại hồ sơ đúng một bản ACTIVE.</summary>
public sealed class WorkflowDefinition
{
    private DefinitionContent? _content;

    private WorkflowDefinition()
    {
    }

    private WorkflowDefinition(Guid id, string documentType, int version, DefinitionContent content, Guid? actor,
        Instant now)
    {
        Id = id;
        DocumentType = documentType;
        Version = version;
        Status = DefinitionStatus.DRAFT;
        CreatedAt = now;
        CreatedBy = actor;
        SetContent(content.WithDocumentType(documentType));
    }

    public Guid Id { get; private set; }

    public string DocumentType { get; private set; } = null!;

    /// <summary>Số version của định nghĩa (1, 2, …) – khác <see cref="RowVersion"/> (optimistic lock).</summary>
    public int Version { get; private set; }

    public DefinitionStatus Status { get; private set; }

    public LocalDate? EffectiveFrom { get; private set; }

    public string DefinitionJson { get; private set; } = null!;

    public ResubmitPolicy ResubmitPolicy { get; private set; }

    public Instant? ActivatedAt { get; private set; }

    public Guid? ActivatedBy { get; private set; }

    public Instant CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public Instant? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public long RowVersion { get; private set; }

    public DefinitionContent Content => _content ??= WorkflowJson.ReadDefinition(DefinitionJson);

    public IReadOnlyList<StepDefinition> Steps => Content.StepList;

    public bool EmitESignOnFinalApprove => Content.EmitESign;

    public static WorkflowDefinition Draft(Guid id, int version, DefinitionContent content, Guid? actor, Instant now) =>
        new(id, content.DocumentType!, version, content, actor, now);

    public WorkflowDefinition CloneAs(Guid newId, int newVersion, Guid? actor, Instant now) =>
        new(newId, DocumentType, newVersion, Content, actor, now);

    public void Update(DefinitionContent newContent, LocalDate? newEffectiveFrom, Guid? actor, Instant now)
    {
        if (Status != DefinitionStatus.DRAFT)
        {
            throw new DomainException(WorkflowError.WF_DEFINITION_NOT_EDITABLE);
        }
        if (newContent.DocumentType is not null && newContent.DocumentType != DocumentType)
        {
            throw new DomainException(WorkflowError.WF_DEFINITION_INVALID, "Cannot change the document type of the definition");
        }
        SetContent(newContent.WithDocumentType(DocumentType));
        EffectiveFrom = newEffectiveFrom;
        UpdatedAt = now;
        UpdatedBy = actor;
    }

    public void Activate(Guid? actor, Instant now)
    {
        if (Status == DefinitionStatus.ACTIVE)
        {
            throw new DomainException(WorkflowError.WF_INVALID_STATE, "Definition is already active");
        }
        Status = DefinitionStatus.ACTIVE;
        ActivatedAt = now;
        ActivatedBy = actor;
        UpdatedAt = now;
        UpdatedBy = actor;
    }

    public void Retire(Guid? actor, Instant now)
    {
        if (Status != DefinitionStatus.ACTIVE)
        {
            throw new DomainException(WorkflowError.WF_INVALID_STATE, "Only an active definition can be retired");
        }
        Status = DefinitionStatus.RETIRED;
        UpdatedAt = now;
        UpdatedBy = actor;
    }

    public IReadOnlyList<StepDefinition> StepsAfter(int afterOrder) => Steps.Where(step => step.Order > afterOrder).ToList();

    public StepDefinition? Step(int order) => Steps.FirstOrDefault(step => step.Order == order);

    private void SetContent(DefinitionContent content)
    {
        _content = content;
        DefinitionJson = WorkflowJson.Write(content);
        ResubmitPolicy = content.Policy;
    }
}
