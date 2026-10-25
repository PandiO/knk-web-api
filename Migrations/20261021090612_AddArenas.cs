using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddArenas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "arenas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LobbyLocationId = table.Column<int>(type: "int", nullable: true),
                    ExitLocationId = table.Column<int>(type: "int", nullable: true),
                    IsDuelEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MaxQueuedMatches = table.Column<int>(type: "int", nullable: false),
                    MaxOffers = table.Column<int>(type: "int", nullable: false),
                    OfferSeconds = table.Column<int>(type: "int", nullable: false),
                    AllowCoinStakes = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AllowItemStakes = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MaxCoinStake = table.Column<int>(type: "int", nullable: false),
                    MaxItemStacks = table.Column<int>(type: "int", nullable: false),
                    MinStakeRatioPercent = table.Column<int>(type: "int", nullable: false),
                    FeePercent = table.Column<int>(type: "int", nullable: false),
                    StakeMinAccountAgeHours = table.Column<int>(type: "int", nullable: true),
                    StakeMinTitleBracketId = table.Column<int>(type: "int", nullable: true),
                    MaxStakedDuelsPerPairPerDay = table.Column<int>(type: "int", nullable: false),
                    PairCooldownMinutes = table.Column<int>(type: "int", nullable: false),
                    DailyCoinWinCap = table.Column<int>(type: "int", nullable: false),
                    AllowOwnGearKeep = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AllowOwnGearDrop = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AllowFixedKit = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AllowKitChoice = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DefaultGearMode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WarmupSeconds = table.Column<int>(type: "int", nullable: false),
                    CountdownSeconds = table.Column<int>(type: "int", nullable: false),
                    FightTimeLimitSeconds = table.Column<int>(type: "int", nullable: false),
                    MaxWaitSeconds = table.Column<int>(type: "int", nullable: false),
                    WinnerLingerSeconds = table.Column<int>(type: "int", nullable: false),
                    AnnounceMatches = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AnnounceFriendly = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.CheckConstraint("CK_arenas_FeePercent_Range", "`FeePercent` >= 0 AND `FeePercent` <= 20");
                    table.CheckConstraint("CK_arenas_FightTimeLimitSeconds_NonNegative", "`FightTimeLimitSeconds` >= 0");
                    table.CheckConstraint("CK_arenas_MaxOffers_Range", "`MaxOffers` >= 1 AND `MaxOffers` <= 6");
                    table.CheckConstraint("CK_arenas_MinStakeRatioPercent_Range", "`MinStakeRatioPercent` >= 0 AND `MinStakeRatioPercent` <= 100");
                    table.ForeignKey(
                        name: "FK_arenas_locations_ExitLocationId",
                        column: x => x.ExitLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_arenas_locations_LobbyLocationId",
                        column: x => x.LobbyLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_arenas_structures_Id",
                        column: x => x.Id,
                        principalTable: "structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_arenas_title_brackets_StakeMinTitleBracketId",
                        column: x => x.StakeMinTitleBracketId,
                        principalTable: "title_brackets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "arena_battlegrounds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    ArenaId = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SpawnALocationId = table.Column<int>(type: "int", nullable: true),
                    SpawnBLocationId = table.Column<int>(type: "int", nullable: true)
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
                name: "arena_kits",
                columns: table => new
                {
                    ArenaId = table.Column<int>(type: "int", nullable: false),
                    KitId = table.Column<int>(type: "int", nullable: false),
                    SequenceNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_arena_kits", x => new { x.ArenaId, x.KitId });
                    table.ForeignKey(
                        name: "FK_arena_kits_arenas_ArenaId",
                        column: x => x.ArenaId,
                        principalTable: "arenas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_arena_kits_kits_KitId",
                        column: x => x.KitId,
                        principalTable: "kits",
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
                name: "IX_arena_kits_KitId",
                table: "arena_kits",
                column: "KitId");

            migrationBuilder.CreateIndex(
                name: "IX_arena_spectator_areas_ArenaId",
                table: "arena_spectator_areas",
                column: "ArenaId");

            migrationBuilder.CreateIndex(
                name: "IX_arenas_ExitLocationId",
                table: "arenas",
                column: "ExitLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_arenas_Key",
                table: "arenas",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_arenas_LobbyLocationId",
                table: "arenas",
                column: "LobbyLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_arenas_StakeMinTitleBracketId",
                table: "arenas",
                column: "StakeMinTitleBracketId");

            // Discovery rules for the arena types (DESIGN.md §4.1): an arena and a spectator area reward like a
            // Structure; battlegrounds are off. A type without a row would read as disabled. Existing rows are kept.
            migrationBuilder.Sql(@"
INSERT IGNORE INTO discovery_reward_rules
    (DomainType, IsEnabled, ExpUnitsMin, ExpUnitsMax, CoinSalaryHoursMin, CoinSalaryHoursMax, GemsMin, GemsMax, IncludeAncestors, UpdatedAt)
VALUES
    ('Arena', 1, 0.05, 0.25, 0.05, 0.25, 0, 0, 1, '2026-10-21 00:00:00'),
    ('ArenaSpectatorArea', 1, 0.05, 0.25, 0.05, 0.25, 0, 0, 1, '2026-10-21 00:00:00'),
    ('ArenaBattleground', 0, 0, 0, 0, 0, 0, 0, 0, '2026-10-21 00:00:00');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM discovery_reward_rules WHERE DomainType IN ('Arena', 'ArenaSpectatorArea', 'ArenaBattleground');");

            migrationBuilder.DropTable(
                name: "arena_battlegrounds");

            migrationBuilder.DropTable(
                name: "arena_kits");

            migrationBuilder.DropTable(
                name: "arena_spectator_areas");

            migrationBuilder.DropTable(
                name: "arenas");
        }
    }
}
