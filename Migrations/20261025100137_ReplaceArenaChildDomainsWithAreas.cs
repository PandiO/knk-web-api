using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceArenaChildDomainsWithAreas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_arenas_locations_LobbyLocationId",
                table: "arenas");

            migrationBuilder.DropForeignKey(
                name: "FK_duel_matches_arena_battlegrounds_BattlegroundId",
                table: "duel_matches");

            // Arena duels rev. 6 (DESIGN.md §4.1): battlegrounds and spectator areas stop being domains. No arena was seeded
            // (developer, 2026-10-21), so their rows are dropped, not converted: matches lose their battleground link, and
            // the child domains' base rows in `domains` go (their subtype rows and sub-regions cascade).
            migrationBuilder.Sql("UPDATE duel_matches SET BattlegroundId = NULL WHERE BattlegroundId IS NOT NULL;");
            migrationBuilder.Sql("DELETE d FROM domains d JOIN arena_battlegrounds b ON b.Id = d.Id;");
            migrationBuilder.Sql("DELETE d FROM domains d JOIN arena_spectator_areas a ON a.Id = d.Id;");
            migrationBuilder.Sql("DELETE FROM discovery_reward_rules WHERE DomainType IN ('ArenaSpectatorArea', 'ArenaBattleground');");

            migrationBuilder.DropTable(
                name: "arena_battlegrounds");

            migrationBuilder.DropTable(
                name: "arena_spectator_areas");

            migrationBuilder.DropIndex(
                name: "IX_arenas_LobbyLocationId",
                table: "arenas");

            migrationBuilder.DropColumn(
                name: "LobbyLocationId",
                table: "arenas");

            migrationBuilder.CreateTable(
                name: "arena_areas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ArenaId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SpawnALocationId = table.Column<int>(type: "int", nullable: true),
                    SpawnBLocationId = table.Column<int>(type: "int", nullable: true),
                    ArrivalLocationId = table.Column<int>(type: "int", nullable: true),
                    TeleportMinTitleBracketId = table.Column<int>(type: "int", nullable: true),
                    TeleportPriceGems = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_arena_areas", x => x.Id);
                    table.CheckConstraint("CK_arena_areas_TeleportPriceGems_NonNegative", "`TeleportPriceGems` IS NULL OR `TeleportPriceGems` >= 0");
                    table.ForeignKey(
                        name: "FK_arena_areas_arenas_ArenaId",
                        column: x => x.ArenaId,
                        principalTable: "arenas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_arena_areas_locations_ArrivalLocationId",
                        column: x => x.ArrivalLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_arena_areas_locations_SpawnALocationId",
                        column: x => x.SpawnALocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_arena_areas_locations_SpawnBLocationId",
                        column: x => x.SpawnBLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_arena_areas_title_brackets_TeleportMinTitleBracketId",
                        column: x => x.TeleportMinTitleBracketId,
                        principalTable: "title_brackets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "arena_area_regions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ArenaAreaId = table.Column<int>(type: "int", nullable: false),
                    WgRegionId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WorldName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_arena_area_regions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_arena_area_regions_arena_areas_ArenaAreaId",
                        column: x => x.ArenaAreaId,
                        principalTable: "arena_areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_arena_area_regions_ArenaAreaId",
                table: "arena_area_regions",
                column: "ArenaAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_arena_area_regions_WorldName_WgRegionId",
                table: "arena_area_regions",
                columns: new[] { "WorldName", "WgRegionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_arena_areas_ArenaId_Key",
                table: "arena_areas",
                columns: new[] { "ArenaId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_arena_areas_ArrivalLocationId",
                table: "arena_areas",
                column: "ArrivalLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_arena_areas_SpawnALocationId",
                table: "arena_areas",
                column: "SpawnALocationId");

            migrationBuilder.CreateIndex(
                name: "IX_arena_areas_SpawnBLocationId",
                table: "arena_areas",
                column: "SpawnBLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_arena_areas_TeleportMinTitleBracketId",
                table: "arena_areas",
                column: "TeleportMinTitleBracketId");

            migrationBuilder.AddForeignKey(
                name: "FK_duel_matches_arena_areas_BattlegroundId",
                table: "duel_matches",
                column: "BattlegroundId",
                principalTable: "arena_areas",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_duel_matches_arena_areas_BattlegroundId",
                table: "duel_matches");

            migrationBuilder.DropTable(
                name: "arena_area_regions");

            migrationBuilder.DropTable(
                name: "arena_areas");

            migrationBuilder.AddColumn<int>(
                name: "LobbyLocationId",
                table: "arenas",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "arena_battlegrounds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    ArenaId = table.Column<int>(type: "int", nullable: false),
                    SpawnALocationId = table.Column<int>(type: "int", nullable: true),
                    SpawnBLocationId = table.Column<int>(type: "int", nullable: true),
                    IsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_arena_battlegrounds_arenas_ArenaId",
                        column: x => x.ArenaId,
                        principalTable: "arenas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_arena_battlegrounds_domains_Id",
                        column: x => x.Id,
                        principalTable: "domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_arena_battlegrounds_locations_SpawnALocationId",
                        column: x => x.SpawnALocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_arena_battlegrounds_locations_SpawnBLocationId",
                        column: x => x.SpawnBLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "arena_spectator_areas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    ArenaId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_arena_spectator_areas_arenas_ArenaId",
                        column: x => x.ArenaId,
                        principalTable: "arenas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_arena_spectator_areas_domains_Id",
                        column: x => x.Id,
                        principalTable: "domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_arenas_LobbyLocationId",
                table: "arenas",
                column: "LobbyLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_arena_battlegrounds_ArenaId_Key",
                table: "arena_battlegrounds",
                columns: new[] { "ArenaId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_arena_battlegrounds_SpawnALocationId",
                table: "arena_battlegrounds",
                column: "SpawnALocationId");

            migrationBuilder.CreateIndex(
                name: "IX_arena_battlegrounds_SpawnBLocationId",
                table: "arena_battlegrounds",
                column: "SpawnBLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_arena_spectator_areas_ArenaId",
                table: "arena_spectator_areas",
                column: "ArenaId");

            migrationBuilder.AddForeignKey(
                name: "FK_arenas_locations_LobbyLocationId",
                table: "arenas",
                column: "LobbyLocationId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_duel_matches_arena_battlegrounds_BattlegroundId",
                table: "duel_matches",
                column: "BattlegroundId",
                principalTable: "arena_battlegrounds",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql(@"
INSERT IGNORE INTO discovery_reward_rules
    (DomainType, IsEnabled, ExpUnitsMin, ExpUnitsMax, CoinSalaryHoursMin, CoinSalaryHoursMax, GemsMin, GemsMax, IncludeAncestors, UpdatedAt)
VALUES
    ('ArenaSpectatorArea', 1, 0.05, 0.25, 0.05, 0.25, 0, 0, 1, '2026-10-21 00:00:00'),
    ('ArenaBattleground', 0, 0, 0, 0, 0, 0, 0, 0, '2026-10-21 00:00:00');");
        }
    }
}
