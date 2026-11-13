using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Notifications;

namespace Platform.Infrastructure.Persistence.Configurations;

public class MessagingScheduleConfiguration : IEntityTypeConfiguration<MessagingSchedule>
{
    public void Configure(EntityTypeBuilder<MessagingSchedule> builder)
    {
        builder.ToTable("MessagingSchedules");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.ReminderTimes).IsRequired().HasMaxLength(100);
        builder.Property(s => s.DailyReportTime).IsRequired().HasMaxLength(5);
        builder.Property(s => s.WeeklyReportTime).IsRequired().HasMaxLength(5);
        builder.Property(s => s.MonthlyReportTime).IsRequired().HasMaxLength(5);
        builder.Property(s => s.WeeklyReportDay).HasConversion<int>();
    }
}
