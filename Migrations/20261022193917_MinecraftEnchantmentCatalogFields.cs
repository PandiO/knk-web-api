using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class MinecraftEnchantmentCatalogFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CatalogStatus",
                table: "minecraftenchantmentrefs",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unverified", // KNG-250: existing rows have never been seen in a scan
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "FirstSeenVersion",
                table: "minecraftenchantmentrefs",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "LastScanId",
                table: "minecraftenchantmentrefs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSeenVersion",
                table: "minecraftenchantmentrefs",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "EnchantmentCount",
                table: "minecraft_catalog_scans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EnchantmentDefinitionsCreated",
                table: "minecraft_catalog_scans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EnchantmentDefinitionsLinked",
                table: "minecraft_catalog_scans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EnchantmentsDeleted",
                table: "minecraft_catalog_scans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EnchantmentsInserted",
                table: "minecraft_catalog_scans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EnchantmentsMarkedMissing",
                table: "minecraft_catalog_scans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EnchantmentsMerged",
                table: "minecraft_catalog_scans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EnchantmentsUnchanged",
                table: "minecraft_catalog_scans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EnchantmentsUpdated",
                table: "minecraft_catalog_scans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_minecraftenchantmentrefs_CatalogStatus",
                table: "minecraftenchantmentrefs",
                column: "CatalogStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_minecraftenchantmentrefs_CatalogStatus",
                table: "minecraftenchantmentrefs");

            migrationBuilder.DropColumn(
                name: "CatalogStatus",
                table: "minecraftenchantmentrefs");

            migrationBuilder.DropColumn(
                name: "FirstSeenVersion",
                table: "minecraftenchantmentrefs");

            migrationBuilder.DropColumn(
                name: "LastScanId",
                table: "minecraftenchantmentrefs");

            migrationBuilder.DropColumn(
                name: "LastSeenVersion",
                table: "minecraftenchantmentrefs");

            migrationBuilder.DropColumn(
                name: "EnchantmentCount",
                table: "minecraft_catalog_scans");

            migrationBuilder.DropColumn(
                name: "EnchantmentDefinitionsCreated",
                table: "minecraft_catalog_scans");

            migrationBuilder.DropColumn(
                name: "EnchantmentDefinitionsLinked",
                table: "minecraft_catalog_scans");

            migrationBuilder.DropColumn(
                name: "EnchantmentsDeleted",
                table: "minecraft_catalog_scans");

            migrationBuilder.DropColumn(
                name: "EnchantmentsInserted",
                table: "minecraft_catalog_scans");

            migrationBuilder.DropColumn(
                name: "EnchantmentsMarkedMissing",
                table: "minecraft_catalog_scans");

            migrationBuilder.DropColumn(
                name: "EnchantmentsMerged",
                table: "minecraft_catalog_scans");

            migrationBuilder.DropColumn(
                name: "EnchantmentsUnchanged",
                table: "minecraft_catalog_scans");

            migrationBuilder.DropColumn(
                name: "EnchantmentsUpdated",
                table: "minecraft_catalog_scans");
        }
    }
}
