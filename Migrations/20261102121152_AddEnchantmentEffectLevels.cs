using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddEnchantmentEffectLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EffectLevel",
                table: "EnchantmentDefinitions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SecondaryEffectDurationSeconds",
                table: "EnchantmentDefinitions",
                type: "double",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EffectLevel",
                table: "EnchantmentDefinitions");

            migrationBuilder.DropColumn(
                name: "SecondaryEffectDurationSeconds",
                table: "EnchantmentDefinitions");
        }
    }
}
