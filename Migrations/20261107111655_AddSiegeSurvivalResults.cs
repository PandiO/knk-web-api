using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddSiegeSurvivalResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "siege_survival_match_checkpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeMatchId = table.Column<int>(type: "int", nullable: false),
                    SiegeScenarioId = table.Column<int>(type: "int", nullable: false),
                    DifficultyProfileId = table.Column<int>(type: "int", nullable: true),
                    CapturedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Substate = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Wave = table.Column<int>(type: "int", nullable: false),
                    CompletedWave = table.Column<int>(type: "int", nullable: false),
                    AliveEnemies = table.Column<int>(type: "int", nullable: false),
                    QueuedEnemies = table.Column<int>(type: "int", nullable: false),
                    Players = table.Column<int>(type: "int", nullable: false),
                    Ghosts = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_survival_match_checkpoints_siege_matches_SiegeMatchId",
                        column: x => x.SiegeMatchId,
                        principalTable: "siege_matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_survival_match_results",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeMatchId = table.Column<int>(type: "int", nullable: false),
                    SiegeLobbyId = table.Column<int>(type: "int", nullable: false),
                    SiegeScenarioId = table.Column<int>(type: "int", nullable: false),
                    DifficultyProfileId = table.Column<int>(type: "int", nullable: true),
                    ConfigurationGeneratedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EquipmentMode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EndReason = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReachedWave = table.Column<int>(type: "int", nullable: false),
                    CompletedWave = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    PlayersAtStart = table.Column<int>(type: "int", nullable: false),
                    PlayersAtEnd = table.Column<int>(type: "int", nullable: false),
                    EarnedKills = table.Column<int>(type: "int", nullable: false),
                    SystemKills = table.Column<int>(type: "int", nullable: false),
                    RewardPolicyVersion = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SettlesRewards = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TotalCoinsSettled = table.Column<int>(type: "int", nullable: false),
                    TotalXpSettled = table.Column<int>(type: "int", nullable: false),
                    LedgerKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LedgerPostingPublicId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SettledAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Source = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestFingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RecordedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_survival_match_results_siege_matches_SiegeMatchId",
                        column: x => x.SiegeMatchId,
                        principalTable: "siege_matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_survival_match_player_results",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SurvivalMatchResultId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PresentAtEnd = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LeftEarly = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LeftAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    KitId = table.Column<int>(type: "int", nullable: true),
                    Kills = table.Column<int>(type: "int", nullable: false),
                    BonusKills = table.Column<int>(type: "int", nullable: false),
                    Deaths = table.Column<int>(type: "int", nullable: false),
                    RepairWork = table.Column<double>(type: "double", nullable: false),
                    MatchCurrencyEarned = table.Column<long>(type: "bigint", nullable: false),
                    MatchCurrencySpent = table.Column<long>(type: "bigint", nullable: false),
                    FinalMatchBalance = table.Column<long>(type: "bigint", nullable: false),
                    CoinsAccrued = table.Column<decimal>(type: "decimal(14,4)", precision: 14, scale: 4, nullable: false),
                    XpAccrued = table.Column<decimal>(type: "decimal(14,4)", precision: 14, scale: 4, nullable: false),
                    CoinsSettled = table.Column<int>(type: "int", nullable: false),
                    XpSettled = table.Column<int>(type: "int", nullable: false),
                    MysteryBoxRolls = table.Column<int>(type: "int", nullable: false),
                    MysteryBoxClaims = table.Column<int>(type: "int", nullable: false),
                    MysteryBoxForfeits = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_survival_match_player_results_siege_survival_match_res~",
                        column: x => x.SurvivalMatchResultId,
                        principalTable: "siege_survival_match_results",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_siege_survival_match_player_results_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_match_checkpoints_SiegeMatchId",
                table: "siege_survival_match_checkpoints",
                column: "SiegeMatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_match_player_results_SurvivalMatchResultId_Us~",
                table: "siege_survival_match_player_results",
                columns: new[] { "SurvivalMatchResultId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_match_player_results_UserId",
                table: "siege_survival_match_player_results",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_match_results_EndedAt",
                table: "siege_survival_match_results",
                column: "EndedAt");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_match_results_SiegeMatchId",
                table: "siege_survival_match_results",
                column: "SiegeMatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_match_results_SiegeScenarioId_EndedAt",
                table: "siege_survival_match_results",
                columns: new[] { "SiegeScenarioId", "EndedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "siege_survival_match_checkpoints");

            migrationBuilder.DropTable(
                name: "siege_survival_match_player_results");

            migrationBuilder.DropTable(
                name: "siege_survival_match_results");
        }
    }
}
