using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Infrastructure.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddFormSectionsAndStageProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "FormDefinitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "FormDefinitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Formula",
                table: "FieldDefinitions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptionsProvider",
                table: "FieldDefinitions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RowFilterJson",
                table: "FieldDefinitions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SectionId",
                table: "FieldDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisibleWhenAttribute",
                table: "FieldDefinitions",
                type: "character varying(63)",
                maxLength: 63,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FormSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FormVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TitleAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TitleEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MinBlocks = table.Column<int>(type: "integer", nullable: false),
                    MaxBlocks = table.Column<int>(type: "integer", nullable: false),
                    ChildTableName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormSections_FormVersions_FormVersionId",
                        column: x => x.FormVersionId,
                        principalTable: "FormVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StageBaselines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SubstepCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LevelId = table.Column<Guid>(type: "uuid", nullable: true),
                    Side = table.Column<int>(type: "integer", nullable: true),
                    EarnedDays = table.Column<int>(type: "integer", nullable: false),
                    Finished = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageBaselines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StageUnitReopenings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SubstepCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LevelId = table.Column<Guid>(type: "uuid", nullable: true),
                    Side = table.Column<int>(type: "integer", nullable: true),
                    ReopenedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageUnitReopenings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FieldDefinitions_SectionId",
                table: "FieldDefinitions",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_FormSections_FormVersionId_Code",
                table: "FormSections",
                columns: new[] { "FormVersionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StageBaselines_ProjectId",
                table: "StageBaselines",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_StageUnitReopenings_ProjectId",
                table: "StageUnitReopenings",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_FieldDefinitions_FormSections_SectionId",
                table: "FieldDefinitions",
                column: "SectionId",
                principalTable: "FormSections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FieldDefinitions_FormSections_SectionId",
                table: "FieldDefinitions");

            migrationBuilder.DropTable(
                name: "FormSections");

            migrationBuilder.DropTable(
                name: "StageBaselines");

            migrationBuilder.DropTable(
                name: "StageUnitReopenings");

            migrationBuilder.DropIndex(
                name: "IX_FieldDefinitions_SectionId",
                table: "FieldDefinitions");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "FormDefinitions");

            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "FormDefinitions");

            migrationBuilder.DropColumn(
                name: "Formula",
                table: "FieldDefinitions");

            migrationBuilder.DropColumn(
                name: "OptionsProvider",
                table: "FieldDefinitions");

            migrationBuilder.DropColumn(
                name: "RowFilterJson",
                table: "FieldDefinitions");

            migrationBuilder.DropColumn(
                name: "SectionId",
                table: "FieldDefinitions");

            migrationBuilder.DropColumn(
                name: "VisibleWhenAttribute",
                table: "FieldDefinitions");
        }
    }
}
