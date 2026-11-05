using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddTelegramNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NotificationLogs_AssignmentId_Kind_ForDate",
                table: "NotificationLogs");

            migrationBuilder.AddColumn<long>(
                name: "TelegramChatId",
                table: "Users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TelegramLinkCode",
                table: "Users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TelegramLinkCodeExpiresAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_TelegramLinkCode",
                table: "Users",
                column: "TelegramLinkCode",
                unique: true,
                filter: "\"TelegramLinkCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_AssignmentId_Kind_ForDate_Channel",
                table: "NotificationLogs",
                columns: new[] { "AssignmentId", "Kind", "ForDate", "Channel" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_TelegramLinkCode",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_NotificationLogs_AssignmentId_Kind_ForDate_Channel",
                table: "NotificationLogs");

            migrationBuilder.DropColumn(
                name: "TelegramChatId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TelegramLinkCode",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TelegramLinkCodeExpiresAtUtc",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_AssignmentId_Kind_ForDate",
                table: "NotificationLogs",
                columns: new[] { "AssignmentId", "Kind", "ForDate" },
                unique: true);
        }
    }
}
