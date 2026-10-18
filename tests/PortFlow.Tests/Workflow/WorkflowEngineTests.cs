using System.Net;
using PortFlow.Shared.Domain;
using PortFlow.Tests.Support;
using PortFlow.Tests.Support.Security;
using PortFlow.Tests.Workflow.Testing;
using PortFlow.Workflow.Api;
using PortFlow.Workflow.Application;
using PortFlow.Workflow.Domain;

namespace PortFlow.Tests.Workflow;

public class WorkflowEngineTests(IntegrationFixture fixture)
    : WorkflowTestBase(fixture, TestApprovableDocuments.Type, Definition)
{
    private const string Definition = """
        {"documentType": "TEST_DOC", "resubmitPolicy": "RESTART",
         "steps": [
           {"order": 1, "name": "Rà soát Kinh doanh",
            "assignee": {"type": "ROLE", "value": "SALES_MANAGER", "scope": "SAME_DEPARTMENT_AS_SUBMITTER"},
            "onReject": "BACK_TO_SUBMITTER", "slaHours": 24},
           {"order": 2, "name": "Pháp chế", "assignee": {"type": "ROLE", "value": "LEGAL"},
            "onReject": "TERMINATE", "condition": "amount > 500000000", "slaHours": 48},
           {"order": 3, "name": "Ban Giám đốc", "assignee": {"type": "GROUP", "value": "DIRECTOR"},
            "onReject": "TERMINATE", "slaHours": 48}],
         "onFinalApprove": {"emitESign": true}}
        """;

    private const decimal Big = 1200000000m;
    private const decimal Small = 300000000m;

    private static readonly RecordingDocumentHandler Handler = TestApprovableDocuments.Handler;

    private Task<WorkflowStartResult> StartAsync(Guid documentId, decimal amount) =>
        StartAsync(documentId, new Dictionary<string, object?> { ["amount"] = amount });

    private async Task<HttpResponseMessage> ApproveAsync(SeedUser user, Guid instance, string? comment) =>
        await ActAsync(user, instance, await CurrentStepAsync(instance), "approve", comment);

    private async Task<HttpResponseMessage> ActOnCurrentAsync(SeedUser user, Guid instance, string action, string? comment) =>
        await ActAsync(user, instance, await CurrentStepAsync(instance), action, comment);

    private Task<int> DefinitionVersionOfAsync(Guid instance) =>
        ScalarAsync<int>("SELECT definition_version FROM workflow.workflow_instance WHERE id = @instance", new { instance });

    private Task<string> ActiveDefinitionIdAsync() =>
        ScalarAsync<string>("""
            SELECT id::text FROM workflow.workflow_definition WHERE document_type = 'TEST_DOC' AND status = 'ACTIVE'
            """);

    [Fact]
    public async Task SubmitOpensFirstStepForManagerOfSameDepartment()
    {
        var doc = Guid.NewGuid();
        var started = await StartAsync(doc, Big);

        Assert.Equal("RUNNING", started.Status);
        Assert.Equal(1, started.CurrentStepOrder);
        Assert.Equal([SeedUser.SALESMGR.Id], started.AssigneeIds);
        Assert.Equal(["STEP_STARTED:1"], Handler.Calls(doc));
        Assert.Equal(1, await InstanceEventsAsync("StepAssigned", started.InstanceId));

        var item = Assert.Single(await InboxItemsAsync(SeedUser.SALESMGR, started.InstanceId));
        Assert.Equal(1, item.GetProperty("stepOrder").GetInt32());
        Assert.Equal("Dịch vụ xếp dỡ – Cty XYZ", item.GetProperty("title").GetString());
        var counters = await GetJsonAsync(SeedUser.SALESMGR, "/api/v1/inbox/counters");
        Assert.True(counters.GetProperty("total").GetInt64() >= 1);

        var detail = await GetJsonAsync(SeedUser.SALES01, $"/api/v1/workflow-instances/{started.InstanceId}");
        var steps = detail.GetProperty("steps");
        Assert.Equal("CURRENT", steps[0].GetProperty("state").GetString());
        Assert.Equal("BACK_TO_SUBMITTER", steps[0].GetProperty("onReject").GetString());
        Assert.Equal("TERMINATE", steps[1].GetProperty("onReject").GetString());
        Assert.False(steps[2].GetProperty("requiresAll").GetBoolean());
        Assert.Equal("REMAINING", steps[1].GetProperty("state").GetString());
        Assert.Equal("REMAINING", steps[2].GetProperty("state").GetString());
        Assert.Equal(2, detail.GetProperty("remainingSteps").GetInt32());
        Assert.False(detail.GetProperty("canAct").GetBoolean());
        Assert.Equal(SeedUser.SALES01.FullName, detail.GetProperty("submitter").GetProperty("fullName").GetString());

        var forbidden = await ClientAs(SeedUser.ACC01).GetAsync($"/api/v1/workflow-instances/{started.InstanceId}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task FullFlowEndsApprovedAndRequestsESign()
    {
        var doc = Guid.NewGuid();
        var instance = (await StartAsync(doc, Big)).InstanceId;

        var first = await ApproveAsync(SeedUser.SALESMGR, instance, "Đồng ý");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await ReadJsonAsync(first);
        Assert.Equal(1, firstBody.GetProperty("completedStep").GetProperty("order").GetInt32());
        Assert.Equal(2, firstBody.GetProperty("currentStep").GetProperty("order").GetInt32());
        Assert.Equal(SeedUser.LEGAL01.FullName,
            firstBody.GetProperty("currentStep").GetProperty("assignees")[0].GetProperty("fullName").GetString());

        var second = await ApproveAsync(SeedUser.LEGAL01, instance, null);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(3, (await ReadJsonAsync(second)).GetProperty("currentStep").GetProperty("order").GetInt32());

        var last = await ApproveAsync(SeedUser.DIRECTOR2, instance, "OK");
        Assert.Equal(HttpStatusCode.OK, last.StatusCode);
        var lastBody = await ReadJsonAsync(last);
        Assert.Equal("APPROVED", lastBody.GetProperty("status").GetString());
        Assert.False(lastBody.TryGetProperty("currentStep", out var current) && current.ValueKind != System.Text.Json.JsonValueKind.Null);

        Assert.Equal(["STEP_STARTED:1", "STEP_STARTED:2", "STEP_STARTED:3", "APPROVED:eSign=true"], Handler.Calls(doc));
        Assert.Equal(1, await InstanceEventsAsync("DocumentApproved", instance));
        Assert.Equal(3, await InstanceEventsAsync("StepCompleted", instance));
        var progress = await WithServiceAsync<IWorkflowService, WorkflowProgress?>(w => w.ProgressOfAsync(DocumentType, doc));
        Assert.NotNull(progress);
        Assert.Equal("APPROVED", progress.Status);
        Assert.Equal(3, progress.DoneSteps);
    }

    [Fact]
    public async Task SmallAmountSkipsLegalStep()
    {
        var instance = (await StartAsync(Guid.NewGuid(), Small)).InstanceId;

        var approved = await ApproveAsync(SeedUser.SALESMGR, instance, null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal(3, (await ReadJsonAsync(approved)).GetProperty("currentStep").GetProperty("order").GetInt32());

        var steps = (await GetJsonAsync(SeedUser.SALES01, $"/api/v1/workflow-instances/{instance}")).GetProperty("steps");
        Assert.Equal("SKIPPED", steps[1].GetProperty("state").GetString());
        Assert.Equal("CURRENT", steps[2].GetProperty("state").GetString());
    }

    [Fact]
    public async Task SameRoleButNotAssigneeIsForbiddenAndAudited()
    {
        var instance = (await StartAsync(Guid.NewGuid(), Big)).InstanceId;
        var otherManager = Guid.NewGuid();
        var token = TestJwtIssuer.BearerFor(otherManager, "salesmgr-kt-" + otherManager.ToString()[..8], "KT",
            "SALES_MANAGER");
        var step = await CurrentStepAsync(instance);

        var response = await ClientWith(token).SendAsync(Json(HttpMethod.Post,
            $"/api/v1/workflow-instances/{instance}/steps/{step}/approve", null, Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, await CountAsync("""
            SELECT count(*) FROM shared.event_publication WHERE event_type LIKE '%.ActionDenied,%'
            AND serialized_event LIKE @step
            """, new { step = $"%{step}%" }));
    }

    [Fact]
    public async Task RejectNeedsCommentAndTerminates()
    {
        var doc = Guid.NewGuid();
        var instance = (await StartAsync(doc, Big)).InstanceId;
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(SeedUser.SALESMGR, instance, null)).StatusCode);

        var noComment = await ActOnCurrentAsync(SeedUser.LEGAL01, instance, "reject", null);
        Assert.Equal(HttpStatusCode.UnprocessableContent, noComment.StatusCode);
        Assert.Equal("WF_COMMENT_REQUIRED", await ErrorCodeAsync(noComment));
        var rejected = await ActOnCurrentAsync(SeedUser.LEGAL01, instance, "reject", "Điều khoản trái luật");
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal("REJECTED", (await ReadJsonAsync(rejected)).GetProperty("status").GetString());

        Assert.Equal("REJECTED:Điều khoản trái luật", Handler.Calls(doc)[^1]);
        Assert.Equal(1, await InstanceEventsAsync("DocumentRejected", instance));
    }

    [Fact]
    public async Task RevisionThenResubmitRestartsInNewRound()
    {
        var doc = Guid.NewGuid();
        var instance = (await StartAsync(doc, Big)).InstanceId;
        var roundOneStep = await CurrentStepAsync(instance);
        var revision = await ActOnCurrentAsync(SeedUser.SALESMGR, instance, "request-revision", "Bổ sung phụ lục giá");
        Assert.Equal(HttpStatusCode.OK, revision.StatusCode);
        Assert.Equal("REVISION_REQUESTED", (await ReadJsonAsync(revision)).GetProperty("status").GetString());
        Assert.Equal("REVISION_REQUESTED:Bổ sung phụ lục giá", Handler.Calls(doc)[^1]);

        var vars = new Dictionary<string, object?> { ["amount"] = Big };
        var ex = await Assert.ThrowsAsync<DomainException>(() => WithServiceAsync<IWorkflowService, WorkflowStartResult>(
            w => w.ResubmitAsync(DocumentType, doc, SeedUser.SALESMGR.Id, vars)));
        Assert.Equal(WorkflowError.WF_NOT_SUBMITTER, ex.Code);
        var resubmitted = await WithServiceAsync<IWorkflowService, WorkflowStartResult>(
            w => w.ResubmitAsync(DocumentType, doc, SeedUser.SALES01.Id, vars));

        Assert.Equal(2, resubmitted.Round);
        Assert.Equal(1, resubmitted.CurrentStepOrder);
        Assert.Equal(instance, resubmitted.InstanceId);

        var stale = await ActAsync(SeedUser.SALESMGR, instance, roundOneStep, "approve", null);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("WF_STEP_NOT_ACTIVE", await ErrorCodeAsync(stale));
        var byDocument = await GetJsonAsync(SeedUser.SALES01,
            $"/api/v1/workflow-instances?documentType={DocumentType}&documentId={doc}");
        Assert.Equal(2, byDocument[0].GetProperty("round").GetInt32());
        Assert.Equal("REQUEST_REVISION", byDocument[0].GetProperty("history")[0].GetProperty("action").GetString());
        Assert.Equal(1, byDocument[0].GetProperty("history")[0].GetProperty("round").GetInt32());
    }

    [Fact]
    public async Task ParallelDoubleSubmitCreatesOneInstance()
    {
        var doc = Guid.NewGuid();
        var go = new TaskCompletionSource();
        var attempts = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await go.Task;
            try
            {
                await StartAsync(doc, Big);
                return "OK";
            }
            catch (DomainException ex)
            {
                return ex.Code.Code;
            }
        })).ToList();
        go.SetResult();
        var outcomes = await Task.WhenAll(attempts);

        Assert.Equal(["OK", "WF_ALREADY_RUNNING"], outcomes.Order());
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM workflow.workflow_instance WHERE document_id = @doc", new { doc }));
    }

    [Fact]
    public async Task ParallelApprovalsOnlyOneWins()
    {
        var instance = (await StartAsync(Guid.NewGuid(), Small)).InstanceId;
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(SeedUser.SALESMGR, instance, null)).StatusCode);
        var step = await CurrentStepAsync(instance);
        SeedUser[] users = [SeedUser.DIRECTOR, SeedUser.DIRECTOR2];
        var clients = users.Select(ClientAs).ToArray();
        var go = new TaskCompletionSource();
        var requests = Enumerable.Range(0, 20).Select(i => Task.Run(async () =>
        {
            await go.Task;
            var response = await clients[i % 2].SendAsync(Json(HttpMethod.Post,
                $"/api/v1/workflow-instances/{instance}/steps/{step}/approve", "{\"comment\":\"ok\"}",
                Guid.NewGuid().ToString()));
            return response.StatusCode;
        })).ToList();
        go.SetResult();
        var codes = await Task.WhenAll(requests);

        Assert.Single(codes, code => code == HttpStatusCode.OK);
        Assert.Equal(19, codes.Count(code => code == HttpStatusCode.Conflict));
        Assert.Equal(1, await CountAsync(
            "SELECT count(*) FROM workflow.approval_action WHERE step_instance_id = @step AND is_decisive", new { step }));
        Assert.Equal(1, await InstanceEventsAsync("DocumentApproved", instance));
    }

    [Fact]
    public async Task ActivatingNewVersionDoesNotMoveRunningInstances()
    {
        var instance = (await StartAsync(Guid.NewGuid(), Big)).InstanceId;
        var pinned = await DefinitionVersionOfAsync(instance);
        var activeId = await ActiveDefinitionIdAsync();

        var clone = await Admin(HttpMethod.Post, $"/api/v1/admin/workflow-definitions/{activeId}/clone");
        Assert.Equal(HttpStatusCode.Created, clone.StatusCode);
        var cloneBody = await ReadJsonAsync(clone);
        Assert.Equal(pinned + 1, cloneBody.GetProperty("version").GetInt32());
        var activation = await Admin(HttpMethod.Post,
            $"/api/v1/admin/workflow-definitions/{cloneBody.GetProperty("id").GetString()}/activate");
        Assert.Equal(HttpStatusCode.OK, activation.StatusCode);
        Assert.True((await ReadJsonAsync(activation)).GetProperty("runningInstancesOnOldVersions").GetInt64() > 0);
        var running = await GetJsonAsync(SeedUser.ADMIN, $"/api/v1/admin/workflow-definitions/{activeId}/running-instances");
        Assert.True(running.GetProperty("running").GetInt64() > 0);
        var retired = await GetJsonAsync(SeedUser.ADMIN, $"/api/v1/admin/workflow-definitions/{activeId}");
        Assert.Equal("RETIRED", retired.GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(SeedUser.SALESMGR, instance, null)).StatusCode);
        Assert.Equal(pinned, await DefinitionVersionOfAsync(instance));
        var newer = (await StartAsync(Guid.NewGuid(), Big)).InstanceId;
        Assert.Equal(pinned + 1, await DefinitionVersionOfAsync(newer));
    }

    [Fact]
    public async Task HandlerFailureRollsBackTheApproval()
    {
        var doc = Guid.NewGuid();
        var instance = (await StartAsync(doc, Small)).InstanceId;
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(SeedUser.SALESMGR, instance, null)).StatusCode);
        var version = await ScalarAsync<long>("SELECT version FROM workflow.workflow_instance WHERE id = @instance",
            new { instance });
        Handler.FailOnApprove(doc);

        Assert.Equal(HttpStatusCode.InternalServerError, (await ApproveAsync(SeedUser.DIRECTOR, instance, null)).StatusCode);

        Assert.Equal($"RUNNING:3:{version}", await ScalarAsync<string>("""
            SELECT status || ':' || current_step_order || ':' || version FROM workflow.workflow_instance WHERE id = @instance
            """, new { instance }));
        Assert.Equal(0, await CountAsync("""
            SELECT count(*) FROM workflow.approval_action a JOIN workflow.step_instance s ON s.id = a.step_instance_id
            WHERE s.instance_id = @instance AND s.step_order = 3
            """, new { instance }));
    }

    [Fact]
    public async Task RetryWithSameKeyReplaysWithoutSecondVote()
    {
        var instance = (await StartAsync(Guid.NewGuid(), Big)).InstanceId;
        var step = await CurrentStepAsync(instance);
        var key = Guid.NewGuid().ToString();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await ActAsync(SeedUser.SALESMGR, instance, step, "approve", "ok", key);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(2, (await ReadJsonAsync(response)).GetProperty("currentStep").GetProperty("order").GetInt32());
            if (attempt == 1)
            {
                Assert.Equal("true", response.Headers.GetValues("Idempotent-Replayed").Single());
            }
        }
        Assert.Equal(1, await CountAsync("SELECT count(*) FROM workflow.approval_action WHERE step_instance_id = @step",
            new { step }));
    }

    [Fact]
    public async Task OverdueStepIsNotifiedOnce()
    {
        var instance = (await StartAsync(Guid.NewGuid(), Big)).InstanceId;
        var step = await CurrentStepAsync(instance);
        await ExecuteAsync("UPDATE workflow.step_instance SET due_at = now() - interval '1 hour' WHERE id = @step",
            new { step });

        await WithServiceAsync<SlaService, int>(sla => sla.NotifyOverdueAsync(500));
        await WithServiceAsync<SlaService, int>(sla => sla.NotifyOverdueAsync(500));

        Assert.Equal(1, await CountAsync("""
            SELECT count(*) FROM shared.event_publication WHERE event_type LIKE '%.StepOverdue,%'
            AND serialized_event LIKE @step
            """, new { step = $"%{step}%" }));
    }

    [Fact]
    public async Task AdminDefinitionApiValidatesAndSimulates()
    {
        var invalid = await Admin(HttpMethod.Post, "/api/v1/admin/workflow-definitions", """
            {"documentType": "TEST_DOC", "steps": [
              {"order": 1, "name": "Admin tự duyệt", "assignee": {"type": "ROLE", "value": "ADMIN"},
               "onReject": "TERMINATE", "condition": "T(java.lang.Runtime) != null"}]}
            """);
        Assert.Equal(HttpStatusCode.UnprocessableContent, invalid.StatusCode);
        var problem = await ReadJsonAsync(invalid);
        Assert.Equal("WF_DEFINITION_INVALID", problem.GetProperty("errorCode").GetString());
        var codes = problem.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("code").GetString()).ToList();
        Assert.Contains("V6", codes);
        Assert.Contains("V4", codes);
        Assert.Contains("V2", codes);

        var forbidden = await ClientAs(SeedUser.DIRECTOR).GetAsync("/api/v1/admin/workflow-definitions");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var activeId = await ActiveDefinitionIdAsync();
        var simulated = await Admin(HttpMethod.Post, $"/api/v1/admin/workflow-definitions/{activeId}/simulate",
            $$"""{"context": {"amount": 300000000}, "submitterId": "{{SeedUser.SALES01.Id}}"}""");
        Assert.Equal(HttpStatusCode.OK, simulated.StatusCode);
        var path = (await ReadJsonAsync(simulated)).GetProperty("path");
        Assert.True(path[0].GetProperty("applies").GetBoolean());
        Assert.Equal(SeedUser.SALESMGR.FullName, path[0].GetProperty("assignees")[0].GetProperty("fullName").GetString());
        Assert.False(path[1].GetProperty("applies").GetBoolean());
        Assert.Equal("condition=false", path[1].GetProperty("reason").GetString());
        Assert.Equal(2, path[2].GetProperty("assignees").GetArrayLength());

        var edit = await Admin(HttpMethod.Put, $"/api/v1/admin/workflow-definitions/{activeId}", Definition);
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal("WF_DEFINITION_NOT_EDITABLE", await ErrorCodeAsync(edit));
        var validation = await Admin(HttpMethod.Post, $"/api/v1/admin/workflow-definitions/{activeId}/validate");
        Assert.Equal(HttpStatusCode.OK, validation.StatusCode);
        Assert.True((await ReadJsonAsync(validation)).GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task SeededDefinitionsAreActive()
    {
        Assert.Equal(["ADDENDUM", "CONTRACT", "PAYMENT_SHEET", "PRICE_LIST"], await ListAsync<string>("""
            SELECT document_type FROM workflow.workflow_definition WHERE status = 'ACTIVE'
            AND document_type IN ('CONTRACT','ADDENDUM','PRICE_LIST','PAYMENT_SHEET') ORDER BY 1
            """));
    }
}
