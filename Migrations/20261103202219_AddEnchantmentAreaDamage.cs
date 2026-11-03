using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddEnchantmentAreaDamage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AreaDamage",
                table: "EnchantmentDefinitions",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DamageRadiusBlocks",
                table: "EnchantmentDefinitions",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "EffectRadiusBlocks",
                table: "EnchantmentDefinitions",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GateDamage",
                table: "EnchantmentDefinitions",
                type: "double",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AreaDamage",
                table: "EnchantmentDefinitions");

            migrationBuilder.DropColumn(
                name: "DamageRadiusBlocks",
                table: "EnchantmentDefinitions");

            migrationBuilder.DropColumn(
                name: "EffectRadiusBlocks",
                table: "EnchantmentDefinitions");

            migrationBuilder.DropColumn(
                name: "GateDamage",
                table: "EnchantmentDefinitions");
        }
    }
}
