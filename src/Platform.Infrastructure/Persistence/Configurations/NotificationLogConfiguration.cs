using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Notifications;

namespace Platform.Infrastructure.Persistence.Configurations;

public class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.ToTable("NotificationLogs");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Kind).IsRequired().HasMaxLength(40);
        builder.Property(n => n.Channel).IsRequired().HasMaxLength(20);
        builder.Property(n => n.Recipient).IsRequired().HasMaxLength(20);
        builder.Property(n => n.Status).IsRequired().HasMaxLength(20);
        builder.Property(n => n.ProviderMessageId).HasMaxLength(200);
        builder.Property(n => n.Error).HasMaxLength(1000);

        // The send-once guard (see NotificationLog). Rows without an assignment (tests) are
        // not covered - NULLs never collide in a Postgres unique index.
        builder.HasIndex(n => new { n.AssignmentId, n.Kind, n.ForDate }).IsUnique();
        builder.HasIndex(n => n.UserId);
    }
}
