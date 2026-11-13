using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddMessagingSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MessagingSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RemindersEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ReminderTimes = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReminderDays = table.Column<int>(type: "integer", nullable: false),
                    ReminderWhatsApp = table.Column<bool>(type: "boolean", nullable: false),
                    ReminderTelegram = table.Column<bool>(type: "boolean", nullable: false),
                    AssignedEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AssignedWhatsApp = table.Column<bool>(type: "boolean", nullable: false),
                    AssignedTelegram = table.Column<bool>(type: "boolean", nullable: false),
                    DailyReportEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DailyReportTime = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    DailyReportDays = table.Column<int>(type: "integer", nullable: false),
                    WeeklyReportEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    WeeklyReportTime = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    WeeklyReportDay = table.Column<int>(type: "integer", nullable: false),
                    MonthlyReportEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MonthlyReportTime = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessagingSchedules", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessagingSchedules");
        }
    }
}
