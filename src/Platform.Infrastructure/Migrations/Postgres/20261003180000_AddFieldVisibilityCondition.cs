using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddFieldVisibilityCondition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VisibleWhenFieldCode",
                table: "FieldDefinitions",
                type: "character varying(63)",
                maxLength: 63,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisibleWhenValuesJson",
                table: "FieldDefinitions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VisibleWhenFieldCode",
                table: "FieldDefinitions");

            migrationBuilder.DropColumn(
                name: "VisibleWhenValuesJson",
                table: "FieldDefinitions");
        }
    }
}
