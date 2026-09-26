using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddTitleBonusMultipliers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // KNG-16: personal and rank multipliers on title promotion gem/XP bonuses. Neutral
            // 1.0 for every existing user and group (0 would wipe their bonuses).
            migrationBuilder.AddColumn<decimal>(
                name: "PersonalGemBonusMultiplier",
                table: "users",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 1.0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PersonalExpBonusMultiplier",
                table: "users",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 1.0m);

            migrationBuilder.AddColumn<decimal>(
                name: "GemBonusMultiplier",
                table: "permission_groups",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 1.0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpBonusMultiplier",
                table: "permission_groups",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 1.0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PersonalGemBonusMultiplier",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PersonalExpBonusMultiplier",
                table: "users");

            migrationBuilder.DropColumn(
                name: "GemBonusMultiplier",
                table: "permission_groups");

            migrationBuilder.DropColumn(
                name: "ExpBonusMultiplier",
                table: "permission_groups");
        }
    }
}
