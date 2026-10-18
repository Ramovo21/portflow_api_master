using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PortFlow.Identity.Api.Events;
using PortFlow.Shared.Outbox;
using PortFlow.Shared.Persistence;
using PortFlow.Shared.Scheduling;
using PortFlow.Workflow.Api;
using PortFlow.Workflow.Application;
using PortFlow.Workflow.Demo;
using PortFlow.Workflow.Domain;
using PortFlow.Workflow.Infrastructure;

namespace PortFlow.Workflow;

/// <summary>F03 – Workflow engine: quy trình duyệt cấu hình được cho mọi loại hồ sơ.</summary>
public static class WorkflowModule
{
    public static WebApplicationBuilder AddPortFlowWorkflow(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddPortFlowDbContext<WorkflowDbContext>();
        services.AddSingleton<IConstraintErrorMapping, WorkflowConstraintErrorMapping>();
        services.AddSingleton<WorkflowExceptionTranslator>();
        services.AddSingleton<IConditionEvaluator, SpelConditionEvaluator>();
        services.AddScoped<IdentityAssigneeResolver>();
        services.AddScoped<IAssigneeResolver>(sp => sp.GetRequiredService<IdentityAssigneeResolver>());
        services.AddScoped<IAssigneeDirectory>(sp => sp.GetRequiredService<IdentityAssigneeResolver>());

        services.AddScoped<HandlerRegistry>();
        services.AddSingleton<DeniedActionAuditor>();
        services.AddScoped<WorkflowEngine>();
        services.AddScoped<IWorkflowService>(sp => sp.GetRequiredService<WorkflowEngine>());
        services.AddScoped<IStepAuthorization, StepAuthorization>();
        services.AddScoped<DefinitionAdminService>();
        services.AddScoped<InboxQueryService>();
        services.AddScoped<InstanceQueryService>();
        services.AddScoped<SlaService>();
        services.AddDomainEventListener<UserDeactivated, AssigneeDeactivationListener>();
        services.AddScheduledJob<SlaScanner>();

        services.Configure<WorkflowDemoOptions>(builder.Configuration.GetSection(WorkflowDemoOptions.Section));
        if (builder.Configuration.GetValue<bool>(WorkflowDemoOptions.Section + ":Enabled"))
        {
            services.AddSingleton<DemoDocumentHandler>();
            services.AddSingleton<IApprovableDocumentHandler>(sp => sp.GetRequiredService<DemoDocumentHandler>());
            services.AddScoped<WorkflowDemoService>();
        }
        return builder;
    }
}
