using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddStakeholderReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportDeliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReportRecipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReceivesDaily = table.Column<bool>(type: "boolean", nullable: false),
                    ReceivesWeekly = table.Column<bool>(type: "boolean", nullable: false),
                    ReceivesMonthly = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportRecipients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReportRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Trigger = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Analysis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PdfBlobName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PhonePdfBlobName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportDeliveries_ReportRunId_RecipientId",
                table: "ReportDeliveries",
                columns: new[] { "ReportRunId", "RecipientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportRuns_CreatedAtUtc",
                table: "ReportRuns",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ReportRuns_Kind_PeriodStart",
                table: "ReportRuns",
                columns: new[] { "Kind", "PeriodStart" },
                unique: true,
                filter: "\"Trigger\" = 'schedule'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportDeliveries");

            migrationBuilder.DropTable(
                name: "ReportRecipients");

            migrationBuilder.DropTable(
                name: "ReportRuns");
        }
    }
}
