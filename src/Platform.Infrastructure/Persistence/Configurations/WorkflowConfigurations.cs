using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Workflow;

namespace Platform.Infrastructure.Persistence.Configurations;

public class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.ToTable("WorkflowDefinitions");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Code).IsRequired().HasMaxLength(64);
        builder.HasIndex(w => w.Code).IsUnique();
        builder.Property(w => w.Name).IsRequired().HasMaxLength(200);

        builder.HasMany(w => w.States).WithOne().HasForeignKey(s => s.WorkflowDefinitionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(w => w.States).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(w => w.Transitions).WithOne().HasForeignKey(t => t.WorkflowDefinitionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(w => w.Transitions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class WorkflowStateConfiguration : IEntityTypeConfiguration<WorkflowState>
{
    public void Configure(EntityTypeBuilder<WorkflowState> builder)
    {
        builder.ToTable("WorkflowStates");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Code).IsRequired().HasMaxLength(64);
        builder.Property(s => s.Label).IsRequired().HasMaxLength(200);
        builder.HasIndex(s => new { s.WorkflowDefinitionId, s.Code }).IsUnique();
    }
}

public class WorkflowTransitionConfiguration : IEntityTypeConfiguration<WorkflowTransition>
{
    public void Configure(EntityTypeBuilder<WorkflowTransition> builder)
    {
        builder.ToTable("WorkflowTransitions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Code).IsRequired().HasMaxLength(64);
        builder.Property(t => t.Label).IsRequired().HasMaxLength(200);
        builder.HasIndex(t => new { t.WorkflowDefinitionId, t.Code }).IsUnique();

        builder.HasMany(t => t.AllowedRoles).WithOne().HasForeignKey(ar => ar.WorkflowTransitionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.AllowedRoles).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class WorkflowTransitionRoleConfiguration : IEntityTypeConfiguration<WorkflowTransitionRole>
{
    public void Configure(EntityTypeBuilder<WorkflowTransitionRole> builder)
    {
        builder.ToTable("WorkflowTransitionRoles");
        builder.HasKey(ar => new { ar.WorkflowTransitionId, ar.RoleId });
    }
}

public class WorkflowInstanceConfiguration : IEntityTypeConfiguration<WorkflowInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowInstance> builder)
    {
        builder.ToTable("WorkflowInstances");
        builder.HasKey(i => i.Id);
        // One workflow instance per record - a record can't be in two workflows at once.
        builder.HasIndex(i => i.RecordId).IsUnique();

        builder.HasMany(i => i.History).WithOne().HasForeignKey(h => h.WorkflowInstanceId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.History).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class WorkflowInstanceHistoryEntryConfiguration : IEntityTypeConfiguration<WorkflowInstanceHistoryEntry>
{
    public void Configure(EntityTypeBuilder<WorkflowInstanceHistoryEntry> builder)
    {
        builder.ToTable("WorkflowInstanceHistoryEntries");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Comment).HasMaxLength(2000);
    }
}
