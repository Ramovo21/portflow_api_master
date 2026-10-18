using System.Net;
using PortFlow.Shared.Domain;

namespace PortFlow.Workflow.Domain;

public static class WorkflowError
{
    public static readonly ErrorCode WF_UNKNOWN_DOCUMENT_TYPE = new(nameof(WF_UNKNOWN_DOCUMENT_TYPE),
        HttpStatusCode.UnprocessableContent, "Document type is not registered with the workflow");

    public static readonly ErrorCode WF_NO_ACTIVE_DEFINITION = new(nameof(WF_NO_ACTIVE_DEFINITION),
        HttpStatusCode.UnprocessableContent, "Document type has no active workflow definition");

    public static readonly ErrorCode WF_ALREADY_RUNNING = new(nameof(WF_ALREADY_RUNNING),
        HttpStatusCode.Conflict, "Document is already in an approval workflow");

    public static readonly ErrorCode WF_NO_ASSIGNEE = new(nameof(WF_NO_ASSIGNEE),
        HttpStatusCode.UnprocessableContent, "No approver could be found for the step");

    public static readonly ErrorCode WF_CONDITION_ERROR = new(nameof(WF_CONDITION_ERROR),
        HttpStatusCode.UnprocessableContent, "Step condition could not be evaluated");

    public static readonly ErrorCode WF_STEP_NOT_FOUND = new(nameof(WF_STEP_NOT_FOUND),
        HttpStatusCode.NotFound, "Approval step not found");

    public static readonly ErrorCode WF_STEP_NOT_ACTIVE = new(nameof(WF_STEP_NOT_ACTIVE),
        HttpStatusCode.Conflict, "Step is no longer awaiting approval");

    public static readonly ErrorCode WF_INVALID_STATE = new(nameof(WF_INVALID_STATE),
        HttpStatusCode.Conflict, "Workflow state does not allow this action");

    public static readonly ErrorCode WF_CONFLICT = new(nameof(WF_CONFLICT),
        HttpStatusCode.Conflict, "Step was already handled by someone else");

    public static readonly ErrorCode WF_NOT_ASSIGNEE = new(nameof(WF_NOT_ASSIGNEE),
        HttpStatusCode.Forbidden, "You are not the assignee of this step");

    public static readonly ErrorCode WF_NOT_SUBMITTER = new(nameof(WF_NOT_SUBMITTER),
        HttpStatusCode.Forbidden, "Only the submitter may perform this action");

    public static readonly ErrorCode WF_ALREADY_VOTED = new(nameof(WF_ALREADY_VOTED),
        HttpStatusCode.Conflict, "You have already voted on this step");

    public static readonly ErrorCode WF_COMMENT_REQUIRED = new(nameof(WF_COMMENT_REQUIRED),
        HttpStatusCode.UnprocessableContent, "Comment is required");

    public static readonly ErrorCode WF_DEFINITION_INVALID = new(nameof(WF_DEFINITION_INVALID),
        HttpStatusCode.UnprocessableContent, "Workflow definition is not valid");

    public static readonly ErrorCode WF_DEFINITION_NOT_EDITABLE = new(nameof(WF_DEFINITION_NOT_EDITABLE),
        HttpStatusCode.Conflict, "Only a DRAFT definition can be edited");

    public static readonly ErrorCode WF_DEFINITION_NOT_FOUND = new(nameof(WF_DEFINITION_NOT_FOUND),
        HttpStatusCode.NotFound, "Workflow definition not found");

    public static readonly ErrorCode WF_INSTANCE_NOT_FOUND = new(nameof(WF_INSTANCE_NOT_FOUND),
        HttpStatusCode.NotFound, "Workflow instance not found");
}
