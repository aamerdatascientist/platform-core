using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Forms;

namespace Platform.Infrastructure.Persistence.Configurations;

public class SubmissionReceiptConfiguration : IEntityTypeConfiguration<SubmissionReceipt>
{
    public void Configure(EntityTypeBuilder<SubmissionReceipt> builder)
    {
        builder.ToTable("SubmissionReceipts");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.IdempotencyKey).IsRequired().HasMaxLength(64);
        // The "saved only once" lock (redesign §9.6). Never soft-deleted, so a plain unique index is right here.
        builder.HasIndex(r => new { r.UserId, r.IdempotencyKey }).IsUnique();
        builder.HasIndex(r => r.RecordId);
    }
}
