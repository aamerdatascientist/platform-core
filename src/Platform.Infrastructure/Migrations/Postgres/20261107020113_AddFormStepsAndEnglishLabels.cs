using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddFormStepsAndEnglishLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NameEn",
                table: "FormDefinitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LabelEn",
                table: "FieldDefinitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StepId",
                table: "FieldDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FormSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FormVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TitleEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormSteps_FormVersions_FormVersionId",
                        column: x => x.FormVersionId,
                        principalTable: "FormVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FieldDefinitions_StepId",
                table: "FieldDefinitions",
                column: "StepId");

            migrationBuilder.CreateIndex(
                name: "IX_FormSteps_FormVersionId_DisplayOrder",
                table: "FormSteps",
                columns: new[] { "FormVersionId", "DisplayOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_FieldDefinitions_FormSteps_StepId",
                table: "FieldDefinitions",
                column: "StepId",
                principalTable: "FormSteps",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FieldDefinitions_FormSteps_StepId",
                table: "FieldDefinitions");

            migrationBuilder.DropTable(
                name: "FormSteps");

            migrationBuilder.DropIndex(
                name: "IX_FieldDefinitions_StepId",
                table: "FieldDefinitions");

            migrationBuilder.DropColumn(
                name: "NameEn",
                table: "FormDefinitions");

            migrationBuilder.DropColumn(
                name: "LabelEn",
                table: "FieldDefinitions");

            migrationBuilder.DropColumn(
                name: "StepId",
                table: "FieldDefinitions");
        }
    }
}
