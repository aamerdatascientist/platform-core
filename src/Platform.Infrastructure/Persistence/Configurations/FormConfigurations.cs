using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Forms;

namespace Platform.Infrastructure.Persistence.Configurations;

public class FormDefinitionConfiguration : IEntityTypeConfiguration<FormDefinition>
{
    public void Configure(EntityTypeBuilder<FormDefinition> builder)
    {
        builder.ToTable("FormDefinitions");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Code).IsRequired().HasMaxLength(64);
        // Partial index, not a plain unique constraint: FormDefinition is soft-deletable
        // (AuditableEntity.IsDeleted + ApplicationDbContext's global query filter), and a
        // plain unique index doesn't know about that - a soft-deleted row keeps permanently
        // occupying its Code, invisible to every app-level query (including this handler's
        // own AnyAsync uniqueness pre-check, which goes through the same filtered DbSet) but
        // still enforced by Postgres, so re-creating a form with that Code fails with a raw
        // 23505 instead of a clean "already exists" error. Scoping the index to
        // WHERE "IsDeleted" = false is what actually frees the Code once its old row is
        // soft-deleted - the standard fix for unique-business-key + soft-delete.
        builder.HasIndex(f => f.Code).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.Property(f => f.Name).IsRequired().HasMaxLength(200);
        builder.Property(f => f.ModuleName).IsRequired().HasMaxLength(100);
        builder.Property(f => f.TableName).HasMaxLength(128);

        builder.HasMany(f => f.Versions).WithOne().HasForeignKey(v => v.FormDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(f => f.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class FormVersionConfiguration : IEntityTypeConfiguration<FormVersion>
{
    public void Configure(EntityTypeBuilder<FormVersion> builder)
    {
        builder.ToTable("FormVersions");
        builder.HasKey(v => v.Id);
        builder.HasIndex(v => new { v.FormDefinitionId, v.VersionNumber }).IsUnique();

        builder.HasMany(v => v.Fields).WithOne().HasForeignKey(fd => fd.FormVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(v => v.Fields).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class FieldDefinitionConfiguration : IEntityTypeConfiguration<FieldDefinition>
{
    public void Configure(EntityTypeBuilder<FieldDefinition> builder)
    {
        builder.ToTable("FieldDefinitions");
        builder.HasKey(fd => fd.Id);
        builder.Property(fd => fd.Code).IsRequired().HasMaxLength(63);
        builder.Property(fd => fd.Label).IsRequired().HasMaxLength(200);
        // No explicit HasColumnType - an unbounded string (no HasMaxLength) already maps to
        // each provider's own "unlimited text" type by default (nvarchar(max) on SQL Server,
        // text on Npgsql), so leaving it unset gets the right column type on both without
        // provider-conditional code.
        builder.HasIndex(fd => new { fd.FormVersionId, fd.Code }).IsUnique();
    }
}
