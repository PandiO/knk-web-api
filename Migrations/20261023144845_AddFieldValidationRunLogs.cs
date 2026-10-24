using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldValidationRunLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FieldValidationRunLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RuleId = table.Column<int>(type: "int", nullable: false),
                    RuleHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubjectType = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubjectId = table.Column<long>(type: "bigint", nullable: false),
                    InputHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DependencyKey = table.Column<string>(type: "varchar(192)", maxLength: 192, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsValid = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsBlocking = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Message = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PlaceholdersJson = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExecutedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldValidationRunLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FieldValidationRunLogs_FieldValidationRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "FieldValidationRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_FieldValidationRunLogs_DependencyKey",
                table: "FieldValidationRunLogs",
                column: "DependencyKey");

            migrationBuilder.CreateIndex(
                name: "IX_FieldValidationRunLogs_ExecutedAt",
                table: "FieldValidationRunLogs",
                column: "ExecutedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FieldValidationRunLogs_RuleId_SubjectType_SubjectId",
                table: "FieldValidationRunLogs",
                columns: new[] { "RuleId", "SubjectType", "SubjectId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FieldValidationRunLogs");
        }
    }
}
