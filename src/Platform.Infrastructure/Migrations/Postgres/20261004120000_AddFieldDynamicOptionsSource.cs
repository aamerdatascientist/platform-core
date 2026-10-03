using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddFieldDynamicOptionsSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DynamicOptionsSourceFormDefinitionId",
                table: "FieldDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DynamicOptionsSourceFieldCode",
                table: "FieldDefinitions",
                type: "character varying(63)",
                maxLength: 63,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DynamicOptionsSourceFormDefinitionId",
                table: "FieldDefinitions");

            migrationBuilder.DropColumn(
                name: "DynamicOptionsSourceFieldCode",
                table: "FieldDefinitions");
        }
    }
}
