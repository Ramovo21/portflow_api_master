using NodaTime;
using PortFlow.Shared.Events;

namespace PortFlow.Workflow.Api.Events;

[Externalized("portflow.events::workflow.action-denied")]
public sealed record ActionDenied(EventMetadata Metadata, Guid StepInstanceId, Guid UserId, string Reason) : IDomainEvent;

[Externalized("portflow.events::workflow.document-approved")]
public sealed record DocumentApproved(EventMetadata Metadata, Guid InstanceId, string DocumentType, Guid DocumentId,
    Guid SubmitterId, bool RequiresESign, DocumentSummary Summary) : IDomainEvent;

[Externalized("portflow.events::workflow.document-rejected")]
public sealed record DocumentRejected(EventMetadata Metadata, Guid InstanceId, string DocumentType, Guid DocumentId,
    Guid SubmitterId, Guid ActorId, string? Comment, DocumentSummary Summary) : IDomainEvent;

[Externalized("portflow.events::workflow.revision-requested")]
public sealed record RevisionRequested(EventMetadata Metadata, Guid InstanceId, string DocumentType, Guid DocumentId,
    Guid SubmitterId, Guid ActorId, int StepOrder, string? Comment, DocumentSummary Summary) : IDomainEvent;

[Externalized("portflow.events::workflow.step-assigned")]
public sealed record StepAssigned(EventMetadata Metadata, Guid InstanceId, string DocumentType, Guid DocumentId,
    int Round, int StepOrder, string StepName, IReadOnlyList<Guid> AssigneeIds, Instant? DueAt, Guid SubmitterId,
    DocumentSummary Summary) : IDomainEvent;

[Externalized("portflow.events::workflow.step-assignee-deactivated")]
public sealed record StepAssigneeDeactivated(EventMetadata Metadata, Guid InstanceId, Guid StepInstanceId,
    string DocumentType, Guid DocumentId, Guid DeactivatedUserId) : IDomainEvent;

[Externalized("portflow.events::workflow.step-completed")]
public sealed record StepCompleted(EventMetadata Metadata, Guid InstanceId, string DocumentType, Guid DocumentId,
    int Round, int StepOrder, string StepName, Guid? ActorId, Guid? OnBehalfOf, string Decision, string? Comment)
    : IDomainEvent;

[Externalized("portflow.events::workflow.step-overdue")]
public sealed record StepOverdue(EventMetadata Metadata, Guid InstanceId, Guid StepInstanceId, string StepName,
    IReadOnlyList<Guid> AssigneeIds, Instant? DueAt) : IDomainEvent;

[Externalized("portflow.events::workflow.workflow-cancelled")]
public sealed record WorkflowCancelled(EventMetadata Metadata, Guid InstanceId, string DocumentType, Guid DocumentId,
    string? Reason) : IDomainEvent;

[Externalized("portflow.events::workflow.workflow-definition-activated")]
public sealed record WorkflowDefinitionActivated(EventMetadata Metadata, Guid DefinitionId, string DocumentType,
    int Version, long RunningInstancesOnOldVersions) : IDomainEvent;
