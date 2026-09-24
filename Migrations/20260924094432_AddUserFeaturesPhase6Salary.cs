using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddUserFeaturesPhase6Salary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Auto-generated default was 0001-01-01 (default(DateTime)) — that would make every
            // pre-existing user's very next join pay out ~2 million hours of back salary. Add the
            // column with a throwaway placeholder default so the NOT NULL ALTER succeeds against
            // a populated table, then immediately overwrite every existing row to "now" via
            // UTC_TIMESTAMP() (timezone-independent, unlike NOW()/CURRENT_TIMESTAMP which use the
            // server's session timezone) — matching this migration's apply time, not each user's
            // CreatedAt, so rollout doesn't trigger one giant retroactive payout either.
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSalaryPayoutAt",
                table: "users",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.Sql("UPDATE `users` SET `LastSalaryPayoutAt` = UTC_TIMESTAMP(6);");

            // Auto-generated defaults were 0m (default(decimal)) — both models default to a
            // neutral 1.0, and 0 would zero out every existing user's/group's salary formula.
            migrationBuilder.AddColumn<decimal>(
                name: "PersonalSalaryMultiplier",
                table: "users",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 1.0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SalaryMultiplier",
                table: "permission_groups",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 1.0m);

            migrationBuilder.CreateTable(
                name: "salary_configurations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GlobalMultiplier = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_salary_configurations", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "salary_configurations");

            migrationBuilder.DropColumn(
                name: "LastSalaryPayoutAt",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PersonalSalaryMultiplier",
                table: "users");

            migrationBuilder.DropColumn(
                name: "SalaryMultiplier",
                table: "permission_groups");
        }
    }
}
