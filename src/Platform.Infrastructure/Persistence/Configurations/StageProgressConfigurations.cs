using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.StageProgress;

namespace Platform.Infrastructure.Persistence.Configurations;

public class StageBaselineEntryConfiguration : IEntityTypeConfiguration<StageBaselineEntry>
{
    public void Configure(EntityTypeBuilder<StageBaselineEntry> builder)
    {
        builder.ToTable("StageBaselines");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.StageCode).IsRequired().HasMaxLength(100);
        builder.Property(b => b.SubstepCode).HasMaxLength(100);
        builder.HasIndex(b => b.ProjectId);
    }
}

public class StageUnitReopeningConfiguration : IEntityTypeConfiguration<StageUnitReopening>
{
    public void Configure(EntityTypeBuilder<StageUnitReopening> builder)
    {
        builder.ToTable("StageUnitReopenings");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.StageCode).IsRequired().HasMaxLength(100);
        builder.Property(r => r.SubstepCode).HasMaxLength(100);
        builder.Property(r => r.Reason).HasMaxLength(500);
        builder.HasIndex(r => r.ProjectId);
    }
}
