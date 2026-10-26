using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddEnchantmentCombatValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CooldownEnabled",
                table: "EnchantmentDefinitions",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CooldownSeconds",
                table: "EnchantmentDefinitions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "EffectDurationSeconds",
                table: "EnchantmentDefinitions",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LevelMultiplier",
                table: "EnchantmentDefinitions",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ProcChancePerLevelPercent",
                table: "EnchantmentDefinitions",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ProcChancePercent",
                table: "EnchantmentDefinitions",
                type: "double",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CooldownEnabled",
                table: "EnchantmentDefinitions");

            migrationBuilder.DropColumn(
                name: "CooldownSeconds",
                table: "EnchantmentDefinitions");

            migrationBuilder.DropColumn(
                name: "EffectDurationSeconds",
                table: "EnchantmentDefinitions");

            migrationBuilder.DropColumn(
                name: "LevelMultiplier",
                table: "EnchantmentDefinitions");

            migrationBuilder.DropColumn(
                name: "ProcChancePerLevelPercent",
                table: "EnchantmentDefinitions");

            migrationBuilder.DropColumn(
                name: "ProcChancePercent",
                table: "EnchantmentDefinitions");
        }
    }
}
