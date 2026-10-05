using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddPermissionGroupTeleportSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TeleportRequestCooldownSeconds",
                table: "permission_groups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeleportRequestPriceCoins",
                table: "permission_groups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeleportRequestPriceExperience",
                table: "permission_groups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeleportRequestPriceGems",
                table: "permission_groups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeleportRequestPriceMode",
                table: "permission_groups",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TeleportRequestPriceMultiplier",
                table: "permission_groups",
                type: "decimal(9,3)",
                precision: 9,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeleportSpawnCooldownSeconds",
                table: "permission_groups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeleportSpawnPriceCoins",
                table: "permission_groups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeleportSpawnPriceExperience",
                table: "permission_groups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeleportSpawnPriceGems",
                table: "permission_groups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeleportSpawnPriceMode",
                table: "permission_groups",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TeleportWarpCooldownSeconds",
                table: "permission_groups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeleportWarpPriceCoins",
                table: "permission_groups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeleportWarpPriceExperience",
                table: "permission_groups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeleportWarpPriceGems",
                table: "permission_groups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeleportWarpPriceMode",
                table: "permission_groups",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TeleportWarpPriceMultiplier",
                table: "permission_groups",
                type: "decimal(9,3)",
                precision: 9,
                scale: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TeleportRequestCooldownSeconds",
                table: "permission_groups");

            migrationBuilder.DropColumn(
                name: "TeleportRequestPriceCoins",
                table: "permission_groups");

            migrationBuilder.DropColumn(
                name: "TeleportRequestPriceExperience",
                table: "permission_groups");

            migrationBuilder.DropColumn(
                name: "TeleportRequestPriceGems",
                table: "permission_groups");

            migrationBuilder.DropColumn(
                name: "TeleportRequestPriceMode",
                table: "permission_groups");

            migrationBuilder.DropColumn(
                name: "TeleportRequestPriceMultiplier",
                table: "permission_groups");

            migrationBuilder.DropColumn(
                name: "TeleportSpawnCooldownSeconds",
                table: "permission_groups");

            migrationBuilder.DropColumn(
                name: "TeleportSpawnPriceCoins",
                table: "permission_groups");

            migrationBuilder.DropColumn(
                name: "TeleportSpawnPriceExperience",
                table: "permission_groups");

            migrationBuilder.DropColumn(
                name: "TeleportSpawnPriceGems",
                table: "permission_groups");

            migrationBuilder.DropColumn(
                name: "TeleportSpawnPriceMode",
                table: "permission_groups");

            migrationBuilder.DropColumn(
                name: "TeleportWarpCooldownSeconds",
                table: "permission_groups");

            migrationBuilder.DropColumn(
                name: "TeleportWarpPriceCoins",
                table: "permission_groups");

            migrationBuilder.DropColumn(
                name: "TeleportWarpPriceExperience",
                table: "permission_groups");

            migrationBuilder.DropColumn(
                name: "TeleportWarpPriceGems",
                table: "permission_groups");

            migrationBuilder.DropColumn(
                name: "TeleportWarpPriceMode",
                table: "permission_groups");

            migrationBuilder.DropColumn(
                name: "TeleportWarpPriceMultiplier",
                table: "permission_groups");
        }
    }
}
