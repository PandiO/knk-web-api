using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddTerritory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "KingdomId",
                table: "towns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Power",
                table: "towns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProvinceId",
                table: "towns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LoyaltyTownId",
                table: "structures",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TerritoryConflictId",
                table: "structures",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TerritoryFlag",
                table: "structures",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "None",
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "TerritoryFlaggedAt",
                table: "structures",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductionSettingsJson",
                table: "game_settings",
                type: "longtext",
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsCapital",
                table: "districts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "kingdoms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "territory_conflicts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    World = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HolderAKind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HolderAId = table.Column<int>(type: "int", nullable: false),
                    HolderBKind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HolderBId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StillOverlapping = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ChunksJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChunkCount = table.Column<int>(type: "int", nullable: false),
                    MinChunkX = table.Column<int>(type: "int", nullable: false),
                    MinChunkZ = table.Column<int>(type: "int", nullable: false),
                    MaxChunkX = table.Column<int>(type: "int", nullable: false),
                    MaxChunkZ = table.Column<int>(type: "int", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastReportedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Resolution = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AwardedHolderKind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AwardedHolderId = table.Column<int>(type: "int", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ResolvedByUserId = table.Column<int>(type: "int", nullable: true),
                    ResolutionNote = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "crown_lands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    KingdomId = table.Column<int>(type: "int", nullable: false),
                    Range = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_crown_lands_domains_Id",
                        column: x => x.Id,
                        principalTable: "domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_crown_lands_kingdoms_KingdomId",
                        column: x => x.KingdomId,
                        principalTable: "kingdoms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "provinces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    KingdomId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_provinces_kingdoms_KingdomId",
                        column: x => x.KingdomId,
                        principalTable: "kingdoms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_towns_KingdomId",
                table: "towns",
                column: "KingdomId");

            migrationBuilder.CreateIndex(
                name: "IX_towns_ProvinceId",
                table: "towns",
                column: "ProvinceId");

            migrationBuilder.CreateIndex(
                name: "IX_structures_LoyaltyTownId",
                table: "structures",
                column: "LoyaltyTownId");

            migrationBuilder.CreateIndex(
                name: "IX_structures_TerritoryConflictId",
                table: "structures",
                column: "TerritoryConflictId");

            migrationBuilder.CreateIndex(
                name: "IX_crown_lands_KingdomId",
                table: "crown_lands",
                column: "KingdomId");

            migrationBuilder.CreateIndex(
                name: "IX_kingdoms_Name",
                table: "kingdoms",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_provinces_KingdomId_Name",
                table: "provinces",
                columns: new[] { "KingdomId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_territory_conflicts_World_Status",
                table: "territory_conflicts",
                columns: new[] { "World", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_structures_territory_conflicts_TerritoryConflictId",
                table: "structures",
                column: "TerritoryConflictId",
                principalTable: "territory_conflicts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_structures_towns_LoyaltyTownId",
                table: "structures",
                column: "LoyaltyTownId",
                principalTable: "towns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_towns_kingdoms_KingdomId",
                table: "towns",
                column: "KingdomId",
                principalTable: "kingdoms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_towns_provinces_ProvinceId",
                table: "towns",
                column: "ProvinceId",
                principalTable: "provinces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_structures_territory_conflicts_TerritoryConflictId",
                table: "structures");

            migrationBuilder.DropForeignKey(
                name: "FK_structures_towns_LoyaltyTownId",
                table: "structures");

            migrationBuilder.DropForeignKey(
                name: "FK_towns_kingdoms_KingdomId",
                table: "towns");

            migrationBuilder.DropForeignKey(
                name: "FK_towns_provinces_ProvinceId",
                table: "towns");

            migrationBuilder.DropTable(
                name: "crown_lands");

            migrationBuilder.DropTable(
                name: "provinces");

            migrationBuilder.DropTable(
                name: "territory_conflicts");

            migrationBuilder.DropTable(
                name: "kingdoms");

            migrationBuilder.DropIndex(
                name: "IX_towns_KingdomId",
                table: "towns");

            migrationBuilder.DropIndex(
                name: "IX_towns_ProvinceId",
                table: "towns");

            migrationBuilder.DropIndex(
                name: "IX_structures_LoyaltyTownId",
                table: "structures");

            migrationBuilder.DropIndex(
                name: "IX_structures_TerritoryConflictId",
                table: "structures");

            migrationBuilder.DropColumn(
                name: "KingdomId",
                table: "towns");

            migrationBuilder.DropColumn(
                name: "Power",
                table: "towns");

            migrationBuilder.DropColumn(
                name: "ProvinceId",
                table: "towns");

            migrationBuilder.DropColumn(
                name: "LoyaltyTownId",
                table: "structures");

            migrationBuilder.DropColumn(
                name: "TerritoryConflictId",
                table: "structures");

            migrationBuilder.DropColumn(
                name: "TerritoryFlag",
                table: "structures");

            migrationBuilder.DropColumn(
                name: "TerritoryFlaggedAt",
                table: "structures");

            migrationBuilder.DropColumn(
                name: "ProductionSettingsJson",
                table: "game_settings");

            migrationBuilder.DropColumn(
                name: "IsCapital",
                table: "districts");
        }
    }
}
