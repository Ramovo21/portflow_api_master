using NodaTime;

namespace PortFlow.Identity.Domain;

public sealed class Department
{
    private Department()
    {
    }

    public Department(Guid id, string code, string name, Guid? headUserId, Guid? parentId, Instant now, Guid actor)
    {
        Id = id;
        Code = code;
        Name = name;
        HeadUserId = headUserId;
        ParentId = parentId;
        CreatedAt = now;
        CreatedBy = actor;
    }

    public Guid Id { get; private set; }

    /// <summary>Khớp attribute <c>department</c> trong Keycloak.</summary>
    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public Guid? HeadUserId { get; private set; }

    public Guid? ParentId { get; private set; }

    public Instant CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public Instant? UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public long Version { get; private set; }

    public void Update(string code, string name, Guid? headUserId, Guid? parentId, Instant now, Guid actor)
    {
        Code = code;
        Name = name;
        HeadUserId = headUserId;
        ParentId = parentId;
        UpdatedAt = now;
        UpdatedBy = actor;
    }
}

public sealed class DocumentType
{
    private DocumentType()
    {
    }

    public DocumentType(string code, string name, bool requiresEsign, string workflowKey, bool active,
        string submitRequirements)
    {
        Code = code;
        Name = name;
        RequiresEsign = requiresEsign;
        WorkflowKey = workflowKey;
        Active = active;
        SubmitRequirements = submitRequirements;
    }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public bool RequiresEsign { get; private set; }

    public string WorkflowKey { get; private set; } = null!;

    public bool Active { get; private set; }

    /// <summary>JSON (cột jsonb).</summary>
    public string SubmitRequirements { get; private set; } = "{}";

    public long Version { get; private set; }

    public void Update(string name, bool requiresEsign, string workflowKey, bool active, string submitRequirements)
    {
        Name = name;
        RequiresEsign = requiresEsign;
        WorkflowKey = workflowKey;
        Active = active;
        SubmitRequirements = submitRequirements;
    }
}
