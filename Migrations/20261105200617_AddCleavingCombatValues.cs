using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddCleavingCombatValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CooldownPerLevelSeconds",
                table: "EnchantmentDefinitions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GateDamageBonusPerLevel",
                table: "EnchantmentDefinitions",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StrikeMultiplier",
                table: "EnchantmentDefinitions",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StrikeMultiplierPerLevel",
                table: "EnchantmentDefinitions",
                type: "double",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CooldownPerLevelSeconds",
                table: "EnchantmentDefinitions");

            migrationBuilder.DropColumn(
                name: "GateDamageBonusPerLevel",
                table: "EnchantmentDefinitions");

            migrationBuilder.DropColumn(
                name: "StrikeMultiplier",
                table: "EnchantmentDefinitions");

            migrationBuilder.DropColumn(
                name: "StrikeMultiplierPerLevel",
                table: "EnchantmentDefinitions");
        }
    }
}
