using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Common;
using Platform.Domain.Forms;
using Platform.Domain.Identity;

namespace Platform.Infrastructure.Persistence;

/// <summary>
/// Maps ONLY the static platform schema. Dynamic per-form tables are managed
/// out-of-band by DynamicSchemaService and are intentionally invisible to this
/// DbContext's model - EF's migrations for THIS context should never touch them.
/// </summary>
public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    private readonly ICurrentUserService? _currentUserService;

    // ICurrentUserService is optional: design-time tooling (dotnet ef migrations add)
    // constructs this DbContext outside a real HTTP request, where there is no
    // authenticated user to resolve. Runtime DI always supplies a real one.
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentUserService? currentUserService = null)
        : base(options)
    {
        _currentUserService = currentUserService;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<FormDefinition> FormDefinitions => Set<FormDefinition>();
    public DbSet<FormVersion> FormVersions => Set<FormVersion>();
    public DbSet<FieldDefinition> FieldDefinitions => Set<FieldDefinition>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditInformation();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The one place CreatedAtUtc/CreatedByUserId/ModifiedAtUtc/ModifiedByUserId get set -
    /// command handlers never set these themselves, so there's exactly one place to check
    /// when audit data looks wrong. Self-registration (no authenticated user yet) is the
    /// one legitimate case where CreatedByUserId ends up Guid.Empty; every other write goes
    /// through an authenticated request.
    /// </summary>
    private void ApplyAuditInformation()
    {
        var userId = _currentUserService?.UserId ?? Guid.Empty;
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = now;
                    entry.Entity.CreatedByUserId = userId;
                    break;
                case EntityState.Modified:
                    entry.Entity.ModifiedAtUtc = now;
                    entry.Entity.ModifiedByUserId = userId;
                    break;
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Soft-delete filter applied platform-wide for every AuditableEntity subtype,
        // so a single accidental missing ".Where(!IsDeleted)" in a handler can't leak
        // deleted rows - the filter is the safety net, not application code discipline.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(AuditableEntity).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
                var property = System.Linq.Expressions.Expression.Property(parameter, nameof(AuditableEntity.IsDeleted));
                var condition = System.Linq.Expressions.Expression.Lambda(
                    System.Linq.Expressions.Expression.Not(property), parameter);
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(condition);
            }
        }

        base.OnModelCreating(modelBuilder);
    }
}
