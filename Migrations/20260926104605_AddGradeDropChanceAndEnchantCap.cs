using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// Linear KNG-6 (knk-workspace <c>docs/specs/items/GRADE_DROPCHANCE.md</c>): <c>Grade.DropChance</c> and
    /// <c>Grade.EnchantLevelCapDivisor</c>, and a backfill of both for grades 1-5. The startup seeds are
    /// create-only by stars, so grades 1-5 that already exist (every dev DB) would otherwise stay null - and a
    /// null divisor means uncapped. Grades 6-10 are new rows the seeds create with their values.
    /// </summary>
    public partial class AddGradeDropChanceAndEnchantCap : Migration
    {
        /// <summary>(Stars, DropChance percent, EnchantLevelCapDivisor) for the grades that existed before KNG-6.</summary>
        public static readonly (int Stars, decimal DropChance, int EnchantLevelCapDivisor)[] Backfill =
        {
            (1, 70m, 5),
            (2, 60m, 4),
            (3, 40m, 3),
            (4, 25m, 2),
            (5, 15m, 1),
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DropChance",
                table: "grades",
                type: "decimal(7,4)",
                precision: 7,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EnchantLevelCapDivisor",
                table: "grades",
                type: "int",
                nullable: true);

            // Keyed by stars (what v1 stored and what the plugin looks grades up by), not by name or id. A grade
            // 6+ that already exists is left null = uncapped, as decided.
            foreach (var (stars, dropChance, divisor) in Backfill)
            {
                migrationBuilder.Sql(string.Format(CultureInfo.InvariantCulture,
                    "UPDATE `grades` SET `DropChance` = {0}, `EnchantLevelCapDivisor` = {1} WHERE `Stars` = {2};",
                    dropChance, divisor, stars));
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DropChance",
                table: "grades");

            migrationBuilder.DropColumn(
                name: "EnchantLevelCapDivisor",
                table: "grades");
        }
    }
}
