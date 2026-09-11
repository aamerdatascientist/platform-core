using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddWorkflowInstanceConcurrencyToken : Migration
    {
        // xmin is one of Postgres's fixed system columns (alongside oid, cmin, cmax,
        // ctid, tableoid) - every table already has it, and "ALTER TABLE ... ADD COLUMN
        // xmin" fails outright ("column name 'xmin' conflicts with a system column name").
        // The auto-generated Up/Down for this migration tried to add/drop it as if it were
        // an ordinary column - EF's migration scaffolding doesn't know this particular
        // shadow property maps to an already-existing system column, only that the model
        // gained a new property. Mapping WorkflowInstance.xmin as a concurrency token (see
        // WorkflowConfigurations) is a metadata-only change with no real DDL behind it, so
        // both methods are deliberately empty rather than the generated (broken) DDL.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
