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
        builder.Property(f => f.NameEn).HasMaxLength(200);
        builder.Property(f => f.ModuleName).IsRequired().HasMaxLength(100);
        builder.Property(f => f.TableName).HasMaxLength(128);
        builder.Property(f => f.IsSystem).HasDefaultValue(false);
        builder.Property(f => f.IsHidden).HasDefaultValue(false);

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

        builder.HasMany(v => v.Steps).WithOne().HasForeignKey(s => s.FormVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(v => v.Steps).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(v => v.Sections).WithOne().HasForeignKey(s => s.FormVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(v => v.Sections).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>Repeatable sections (2026-11-10): each names a child table of the form's data table.</summary>
public class FormSectionConfiguration : IEntityTypeConfiguration<FormSection>
{
    public void Configure(EntityTypeBuilder<FormSection> builder)
    {
        builder.ToTable("FormSections");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Code).IsRequired().HasMaxLength(40);
        builder.Property(s => s.TitleAr).IsRequired().HasMaxLength(200);
        builder.Property(s => s.TitleEn).HasMaxLength(200);
        builder.Property(s => s.ChildTableName).HasMaxLength(128);
        builder.HasIndex(s => new { s.FormVersionId, s.Code }).IsUnique();
    }
}

/// <summary>Filling steps (redesign §9.2) - presentation metadata, nothing physical.</summary>
public class FormStepConfiguration : IEntityTypeConfiguration<FormStep>
{
    public void Configure(EntityTypeBuilder<FormStep> builder)
    {
        builder.ToTable("FormSteps");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TitleAr).IsRequired().HasMaxLength(200);
        builder.Property(s => s.TitleEn).HasMaxLength(200);
        builder.HasIndex(s => new { s.FormVersionId, s.DisplayOrder });
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
        builder.Property(fd => fd.LabelEn).HasMaxLength(200);
        // A deleted step never leaves a field pointing at nothing: FormVersion.RemoveStep moves
        // its fields first, and SetNull is the database-side backstop.
        builder.HasOne<FormStep>().WithMany().HasForeignKey(fd => fd.StepId).OnDelete(DeleteBehavior.SetNull);
        // Same 63-char cap as Code itself - this stores another field's Code verbatim.
        builder.Property(fd => fd.VisibleWhenFieldCode).HasMaxLength(63);
        builder.Property(fd => fd.FilterByFieldCode).HasMaxLength(63);
        // Same 63-char cap, same reason: stores another field's Code verbatim (on another
        // FORM this time, rather than a same-version sibling, but the cap is identical).
        builder.Property(fd => fd.DynamicOptionsSourceFieldCode).HasMaxLength(63);
        // No explicit HasColumnType - an unbounded string (no HasMaxLength) already maps to
        // each provider's own "unlimited text" type by default (nvarchar(max) on SQL Server,
        // text on Npgsql), so leaving it unset gets the right column type on both without
        // provider-conditional code.
        builder.HasIndex(fd => new { fd.FormVersionId, fd.Code }).IsUnique();
        // FormVersion.RemoveSection refuses while any field is in it; Restrict is the backstop.
        builder.HasOne<FormSection>().WithMany().HasForeignKey(fd => fd.SectionId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(fd => fd.VisibleWhenAttribute).HasMaxLength(63);
        builder.Property(fd => fd.Formula).HasMaxLength(500);
        builder.Property(fd => fd.OptionsProvider).HasMaxLength(100);
    }
}
