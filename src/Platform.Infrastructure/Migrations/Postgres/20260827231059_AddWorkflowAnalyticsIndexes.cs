using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddWorkflowAnalyticsIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkflowInstanceHistoryEntries_WorkflowInstanceId",
                table: "WorkflowInstanceHistoryEntries");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_CurrentStateId",
                table: "WorkflowInstances",
                column: "CurrentStateId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_FormDefinitionId",
                table: "WorkflowInstances",
                column: "FormDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstanceHistoryEntries_WorkflowInstanceId_ExecutedA~",
                table: "WorkflowInstanceHistoryEntries",
                columns: new[] { "WorkflowInstanceId", "ExecutedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkflowInstances_CurrentStateId",
                table: "WorkflowInstances");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowInstances_FormDefinitionId",
                table: "WorkflowInstances");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowInstanceHistoryEntries_WorkflowInstanceId_ExecutedA~",
                table: "WorkflowInstanceHistoryEntries");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstanceHistoryEntries_WorkflowInstanceId",
                table: "WorkflowInstanceHistoryEntries",
                column: "WorkflowInstanceId");
        }
    }
}
