using Microsoft.EntityFrameworkCore;
using Platform.Domain.Forms;
using Platform.Domain.Identity;

namespace Platform.Application.Common.Interfaces;

/// <summary>
/// Covers only the STATIC platform schema (Identity, Form metadata). Dynamic per-form
/// data tables are deliberately excluded from this contract - see IDynamicDataRepository.
/// EF Core's compiled model can't represent tables that don't exist until an
/// administrator publishes a form, so mixing the two here would be misleading.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<Department> Departments { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Platform.Domain.Identity.UserRole> UserRoles { get; }

    DbSet<FormDefinition> FormDefinitions { get; }
    DbSet<FormVersion> FormVersions { get; }
    DbSet<FieldDefinition> FieldDefinitions { get; }
    DbSet<Platform.Domain.Forms.FormDefinitionRole> FormDefinitionRoles { get; }
    DbSet<Platform.Domain.Forms.FormDefinitionUser> FormDefinitionUsers { get; }

    DbSet<Platform.Domain.Workflow.WorkflowDefinition> WorkflowDefinitions { get; }
    DbSet<Platform.Domain.Workflow.WorkflowState> WorkflowStates { get; }
    DbSet<Platform.Domain.Workflow.WorkflowTransition> WorkflowTransitions { get; }
    DbSet<Platform.Domain.Workflow.WorkflowTransitionRole> WorkflowTransitionRoles { get; }
    DbSet<Platform.Domain.Workflow.WorkflowInstance> WorkflowInstances { get; }
    DbSet<Platform.Domain.Workflow.WorkflowInstanceHistoryEntry> WorkflowInstanceHistoryEntries { get; }

    DbSet<Platform.Domain.Files.FileMetadata> FileMetadataEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
