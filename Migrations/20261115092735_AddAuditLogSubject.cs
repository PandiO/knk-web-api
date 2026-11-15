using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// PandiO/knk-workspace#50 (developer decision 2026-11-15): a generic audit subject. Adds nullable SubjectType and
    /// SubjectId to audit_log_entries with an index for a subject's history, and makes TargetUserId nullable so an
    /// entry about a permission group needs no player. Additive: existing rows keep their target and get no subject.
    /// Down turns a null target into 0 (entries about a subject lose their meaning once the columns are gone).
    /// </summary>
    public partial class AddAuditLogSubject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "TargetUserId",
                table: "audit_log_entries",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "SubjectId",
                table: "audit_log_entries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubjectType",
                table: "audit_log_entries",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_entries_SubjectType_SubjectId_Timestamp",
                table: "audit_log_entries",
                columns: new[] { "SubjectType", "SubjectId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_log_entries_SubjectType_SubjectId_Timestamp",
                table: "audit_log_entries");

            migrationBuilder.DropColumn(
                name: "SubjectId",
                table: "audit_log_entries");

            migrationBuilder.DropColumn(
                name: "SubjectType",
                table: "audit_log_entries");

            migrationBuilder.AlterColumn<int>(
                name: "TargetUserId",
                table: "audit_log_entries",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
