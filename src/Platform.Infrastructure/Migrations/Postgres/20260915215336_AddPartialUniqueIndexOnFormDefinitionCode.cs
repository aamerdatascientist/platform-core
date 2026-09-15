using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddPartialUniqueIndexOnFormDefinitionCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FormDefinitions_Code",
                table: "FormDefinitions");

            migrationBuilder.CreateIndex(
                name: "IX_FormDefinitions_Code",
                table: "FormDefinitions",
                column: "Code",
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FormDefinitions_Code",
                table: "FormDefinitions");

            migrationBuilder.CreateIndex(
                name: "IX_FormDefinitions_Code",
                table: "FormDefinitions",
                column: "Code",
                unique: true);
        }
    }
}
