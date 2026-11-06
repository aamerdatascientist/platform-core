using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Reports;

namespace Platform.Infrastructure.Persistence.Configurations;

public class ReportRecipientConfiguration : IEntityTypeConfiguration<ReportRecipient>
{
    public void Configure(EntityTypeBuilder<ReportRecipient> builder)
    {
        builder.ToTable("ReportRecipients");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(200);
        builder.Property(r => r.PhoneNumber).IsRequired().HasMaxLength(20);
    }
}

public class ReportRunConfiguration : IEntityTypeConfiguration<ReportRun>
{
    public void Configure(EntityTypeBuilder<ReportRun> builder)
    {
        builder.ToTable("ReportRuns");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Kind).IsRequired().HasMaxLength(20);
        builder.Property(r => r.Trigger).IsRequired().HasMaxLength(20);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(20);
        builder.Property(r => r.Analysis).HasMaxLength(30);
        builder.Property(r => r.CostUsd).HasPrecision(10, 4);
        builder.Property(r => r.Error).HasMaxLength(2000);
        builder.Property(r => r.PdfBlobName).HasMaxLength(300);
        builder.Property(r => r.PhonePdfBlobName).HasMaxLength(300);

        // The send-once guard for scheduled reports (see ReportRun).
        builder.HasIndex(r => new { r.Kind, r.PeriodStart })
            .IsUnique()
            .HasFilter("\"Trigger\" = 'schedule'");
        // The monthly budget sums cost by creation time.
        builder.HasIndex(r => r.CreatedAtUtc);
    }
}

public class ReportDeliveryConfiguration : IEntityTypeConfiguration<ReportDelivery>
{
    public void Configure(EntityTypeBuilder<ReportDelivery> builder)
    {
        builder.ToTable("ReportDeliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.PhoneNumber).IsRequired().HasMaxLength(20);
        builder.Property(d => d.Status).IsRequired().HasMaxLength(20);
        builder.Property(d => d.ProviderMessageId).HasMaxLength(200);
        builder.Property(d => d.Error).HasMaxLength(1000);
        builder.HasIndex(d => new { d.ReportRunId, d.RecipientId }).IsUnique();
    }
}
