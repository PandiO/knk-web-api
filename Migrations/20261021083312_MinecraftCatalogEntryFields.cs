using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// KNG-200 phase 2 (docs/specs/minecraft-catalog/IMPLEMENTATION_PLAN.md §8 step 1): additive only. Catalogue
    /// columns on minecraftmaterialrefs, MaterialRefId/StateKey/IsPartial on minecraftblockrefs (StateKey not
    /// unique yet), and the minecraft_catalog_scans table. No row is changed or deleted.
    /// </summary>
    public partial class MinecraftCatalogEntryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CatalogStatus",
                table: "minecraftmaterialrefs",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Unverified", // KNG-200: existing rows have never been seen in a scan
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "DefaultBlockState",
                table: "minecraftmaterialrefs",
                type: "longtext",
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "minecraftmaterialrefs",
                type: "varchar(191)",
                maxLength: 191,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "FirstSeenVersion",
                table: "minecraftmaterialrefs",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "IconSource",
                table: "minecraftmaterialrefs",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "IconStatus",
                table: "minecraftmaterialrefs",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "IconUrlOverride",
                table: "minecraftmaterialrefs",
                type: "longtext",
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsBlock",
                table: "minecraftmaterialrefs",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsItem",
                table: "minecraftmaterialrefs",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastScanId",
                table: "minecraftmaterialrefs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSeenVersion",
                table: "minecraftmaterialrefs",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "MaxStackSize",
                table: "minecraftmaterialrefs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPartial",
                table: "minecraftblockrefs",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaterialRefId",
                table: "minecraftblockrefs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StateKey",
                table: "minecraftblockrefs",
                type: "char(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "minecraft_catalog_scans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: true),
                    WorldTaskId = table.Column<int>(type: "int", nullable: true),
                    MinecraftVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ServerVersion = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SchemaVersion = table.Column<int>(type: "int", nullable: true),
                    ContentHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntryCount = table.Column<int>(type: "int", nullable: true),
                    Inserted = table.Column<int>(type: "int", nullable: false),
                    Updated = table.Column<int>(type: "int", nullable: false),
                    Unchanged = table.Column<int>(type: "int", nullable: false),
                    Merged = table.Column<int>(type: "int", nullable: false),
                    Deleted = table.Column<int>(type: "int", nullable: false),
                    MarkedMissing = table.Column<int>(type: "int", nullable: false),
                    IconsMissing = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_minecraftmaterialrefs_CatalogStatus",
                table: "minecraftmaterialrefs",
                column: "CatalogStatus");

            migrationBuilder.CreateIndex(
                name: "IX_minecraftblockrefs_MaterialRefId",
                table: "minecraftblockrefs",
                column: "MaterialRefId");

            migrationBuilder.CreateIndex(
                name: "IX_minecraftblockrefs_StateKey",
                table: "minecraftblockrefs",
                column: "StateKey");

            migrationBuilder.CreateIndex(
                name: "IX_minecraft_catalog_scans_Status",
                table: "minecraft_catalog_scans",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_minecraftblockrefs_minecraftmaterialrefs_MaterialRefId",
                table: "minecraftblockrefs",
                column: "MaterialRefId",
                principalTable: "minecraftmaterialrefs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_minecraftblockrefs_minecraftmaterialrefs_MaterialRefId",
                table: "minecraftblockrefs");

            migrationBuilder.DropTable(
                name: "minecraft_catalog_scans");

            migrationBuilder.DropIndex(
                name: "IX_minecraftmaterialrefs_CatalogStatus",
                table: "minecraftmaterialrefs");

            migrationBuilder.DropIndex(
                name: "IX_minecraftblockrefs_MaterialRefId",
                table: "minecraftblockrefs");

            migrationBuilder.DropIndex(
                name: "IX_minecraftblockrefs_StateKey",
                table: "minecraftblockrefs");

            migrationBuilder.DropColumn(
                name: "CatalogStatus",
                table: "minecraftmaterialrefs");

            migrationBuilder.DropColumn(
                name: "DefaultBlockState",
                table: "minecraftmaterialrefs");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "minecraftmaterialrefs");

            migrationBuilder.DropColumn(
                name: "FirstSeenVersion",
                table: "minecraftmaterialrefs");

            migrationBuilder.DropColumn(
                name: "IconSource",
                table: "minecraftmaterialrefs");

            migrationBuilder.DropColumn(
                name: "IconStatus",
                table: "minecraftmaterialrefs");

            migrationBuilder.DropColumn(
                name: "IconUrlOverride",
                table: "minecraftmaterialrefs");

            migrationBuilder.DropColumn(
                name: "IsBlock",
                table: "minecraftmaterialrefs");

            migrationBuilder.DropColumn(
                name: "IsItem",
                table: "minecraftmaterialrefs");

            migrationBuilder.DropColumn(
                name: "LastScanId",
                table: "minecraftmaterialrefs");

            migrationBuilder.DropColumn(
                name: "LastSeenVersion",
                table: "minecraftmaterialrefs");

            migrationBuilder.DropColumn(
                name: "MaxStackSize",
                table: "minecraftmaterialrefs");

            migrationBuilder.DropColumn(
                name: "IsPartial",
                table: "minecraftblockrefs");

            migrationBuilder.DropColumn(
                name: "MaterialRefId",
                table: "minecraftblockrefs");

            migrationBuilder.DropColumn(
                name: "StateKey",
                table: "minecraftblockrefs");
        }
    }
}
