using Microsoft.EntityFrameworkCore;
using PortFlow.Shared.Persistence;
using PortFlow.Workflow.Domain.Definition;
using PortFlow.Workflow.Domain.Runtime;

namespace PortFlow.Workflow.Infrastructure;

/// <summary>Schema <c>workflow</c> (= WorkflowDefinitionRepository + WorkflowInstanceRepository). Bảng do Flyway tạo.</summary>
public sealed class WorkflowDbContext(DbContextOptions<WorkflowDbContext> options) : DbContext(options)
{
    public DbSet<WorkflowDefinition> Definitions => Set<WorkflowDefinition>();

    public DbSet<WorkflowInstance> Instances => Set<WorkflowInstance>();

    public DbSet<StepInstance> StepInstances => Set<StepInstance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("workflow");

        modelBuilder.Entity<WorkflowDefinition>(definition =>
        {
            definition.ToTable("workflow_definition");
            definition.HasKey(d => d.Id);
            definition.Property(d => d.Id).ValueGeneratedNever();
            definition.Property(d => d.Status).HasConversion<string>();
            definition.Property(d => d.ResubmitPolicy).HasConversion<string>();
            definition.Property(d => d.DefinitionJson).HasColumnName("definition").HasColumnType("jsonb");
            definition.Property(d => d.RowVersion).IsConcurrencyToken();
            definition.Ignore(d => d.Content);
            definition.Ignore(d => d.Steps);
            definition.Ignore(d => d.EmitESignOnFinalApprove);
        });

        modelBuilder.Entity<WorkflowInstance>(instance =>
        {
            instance.ToTable("workflow_instance");
            instance.HasKey(i => i.Id);
            instance.Property(i => i.Id).ValueGeneratedNever();
            instance.Property(i => i.Status).HasConversion<string>();
            instance.Property(i => i.ContextVarsJson).HasColumnName("context_vars").HasColumnType("jsonb");
            instance.Ignore(i => i.ContextVars);
            instance.Ignore(i => i.Steps);
            instance.HasMany<StepInstance>("_steps").WithOne().HasForeignKey("InstanceId").IsRequired();
            instance.Navigation("_steps").AutoInclude();
        });

        modelBuilder.Entity<StepInstance>(step =>
        {
            step.ToTable("step_instance");
            step.HasKey(s => s.Id);
            step.Property(s => s.Id).ValueGeneratedNever();
            step.Property<Guid>("InstanceId").HasColumnName("instance_id");
            step.Property(s => s.OnReject).HasConversion<string>();
            step.Property(s => s.Status).HasConversion<string>();
            step.Property(s => s.Decision).HasConversion<string>();
            step.Ignore(s => s.Actions);
            step.HasMany<ApprovalAction>("_actions").WithOne().HasForeignKey("StepInstanceId").IsRequired();
            step.Navigation("_actions").AutoInclude();
        });

        modelBuilder.Entity<ApprovalAction>(action =>
        {
            action.ToTable("approval_action");
            action.HasKey(a => a.Id);
            action.Property(a => a.Id).ValueGeneratedNever();
            action.Property<Guid>("StepInstanceId").HasColumnName("step_instance_id");
            action.Property(a => a.Action).HasConversion<string>();
            action.Property(a => a.Decisive).HasColumnName("is_decisive");
            action.Ignore(a => a.Voter);
        });

        modelBuilder.ApplyAggregateRootConventions();
    }
}
