using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// Arena duels P2 (KNG-213, knk-workspace docs/specs/arena-duels/DESIGN.md §4.3, §10.3): duel_matches
    /// (append-only match records; unique PublicToken), duel_policies (the review rules, one row seeded with
    /// the design defaults) and duel_restrictions (stakes-only staff bars). The ledger needs no schema change:
    /// SYS_DUEL and the DUEL_* reasons are strings.
    /// </summary>
    public partial class AddDuelMatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "duel_matches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PublicToken = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ArenaId = table.Column<int>(type: "int", nullable: false),
                    BattlegroundId = table.Column<int>(type: "int", nullable: true),
                    ChallengerUserId = table.Column<int>(type: "int", nullable: false),
                    OpponentUserId = table.Column<int>(type: "int", nullable: false),
                    GearMode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FixedKitId = table.Column<int>(type: "int", nullable: true),
                    ChallengerKitId = table.Column<int>(type: "int", nullable: true),
                    OpponentKitId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EndReason = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WinnerUserId = table.Column<int>(type: "int", nullable: true),
                    ChallengerCoins = table.Column<long>(type: "bigint", nullable: false),
                    OpponentCoins = table.Column<long>(type: "bigint", nullable: false),
                    FeePercent = table.Column<int>(type: "int", nullable: false),
                    FeeCoins = table.Column<long>(type: "bigint", nullable: false),
                    StakeItemsJson = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SpoilsItemsJson = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChallengerItemStacks = table.Column<int>(type: "int", nullable: false),
                    OpponentItemStacks = table.Column<int>(type: "int", nullable: false),
                    StakeTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    SettlementTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    FeeTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    RefundTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    Review = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewTier = table.Column<int>(type: "int", nullable: true),
                    ReviewSignals = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewDetailsJson = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AlertId = table.Column<long>(type: "bigint", nullable: true),
                    ReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReviewReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RevertResultJson = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UndeliveredItems = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FightTelemetryJson = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OfferCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LockedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    FightStartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.CheckConstraint("CK_duel_matches_Coins_NonNegative", "`ChallengerCoins` >= 0 AND `OpponentCoins` >= 0 AND `FeeCoins` >= 0");
                    table.CheckConstraint("CK_duel_matches_DistinctPlayers", "`ChallengerUserId` <> `OpponentUserId`");
                    table.ForeignKey(
                        name: "FK_duel_matches_arena_battlegrounds_BattlegroundId",
                        column: x => x.BattlegroundId,
                        principalTable: "arena_battlegrounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_duel_matches_arenas_ArenaId",
                        column: x => x.ArenaId,
                        principalTable: "arenas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_duel_matches_kits_ChallengerKitId",
                        column: x => x.ChallengerKitId,
                        principalTable: "kits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_duel_matches_kits_FixedKitId",
                        column: x => x.FixedKitId,
                        principalTable: "kits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_duel_matches_kits_OpponentKitId",
                        column: x => x.OpponentKitId,
                        principalTable: "kits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_duel_matches_users_ChallengerUserId",
                        column: x => x.ChallengerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_duel_matches_users_OpponentUserId",
                        column: x => x.OpponentUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "duel_policies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    S1Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    S1LoserDamagePercent = table.Column<int>(type: "int", nullable: false),
                    S2Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    S2ShortFightSeconds = table.Column<int>(type: "int", nullable: false),
                    S3Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    S4Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    S4MaxLoserAttackAttempts = table.Column<int>(type: "int", nullable: false),
                    S5Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    S5PairDuels = table.Column<int>(type: "int", nullable: false),
                    S6Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    S6MinYoungOpponents = table.Column<int>(type: "int", nullable: false),
                    S6YoungAccountDays = table.Column<int>(type: "int", nullable: false),
                    S6WindowDays = table.Column<int>(type: "int", nullable: false),
                    EscalationFlagCount = table.Column<int>(type: "int", nullable: false),
                    EscalationWindowDays = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.CheckConstraint("CK_duel_policies_EscalationFlagCount_AtLeastTwo", "`EscalationFlagCount` >= 2");
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "duel_restrictions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    OtherUserId = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    LiftedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LiftedByUserId = table.Column<int>(type: "int", nullable: true),
                    LiftReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_duel_restrictions_users_OtherUserId",
                        column: x => x.OtherUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_duel_restrictions_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.InsertData(
                table: "duel_policies",
                columns: new[] { "Id", "EscalationFlagCount", "EscalationWindowDays", "S1Enabled", "S1LoserDamagePercent", "S2Enabled", "S2ShortFightSeconds", "S3Enabled", "S4Enabled", "S4MaxLoserAttackAttempts", "S5Enabled", "S5PairDuels", "S6Enabled", "S6MinYoungOpponents", "S6WindowDays", "S6YoungAccountDays", "UpdatedAt", "UpdatedByUserId" },
                values: new object[] { 1, 2, 7, true, 10, true, 10, true, true, 0, true, 3, true, 3, 7, 7, new DateTime(2026, 10, 21, 0, 0, 0, 0, DateTimeKind.Utc), null });

            migrationBuilder.CreateIndex(
                name: "IX_duel_matches_ArenaId_Status",
                table: "duel_matches",
                columns: new[] { "ArenaId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_duel_matches_BattlegroundId",
                table: "duel_matches",
                column: "BattlegroundId");

            migrationBuilder.CreateIndex(
                name: "IX_duel_matches_ChallengerKitId",
                table: "duel_matches",
                column: "ChallengerKitId");

            migrationBuilder.CreateIndex(
                name: "IX_duel_matches_ChallengerUserId_CreatedAt",
                table: "duel_matches",
                columns: new[] { "ChallengerUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_duel_matches_FixedKitId",
                table: "duel_matches",
                column: "FixedKitId");

            migrationBuilder.CreateIndex(
                name: "IX_duel_matches_OpponentKitId",
                table: "duel_matches",
                column: "OpponentKitId");

            migrationBuilder.CreateIndex(
                name: "IX_duel_matches_OpponentUserId_CreatedAt",
                table: "duel_matches",
                columns: new[] { "OpponentUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_duel_matches_PublicToken",
                table: "duel_matches",
                column: "PublicToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_duel_matches_Review",
                table: "duel_matches",
                column: "Review");

            migrationBuilder.CreateIndex(
                name: "IX_duel_matches_SettlementTransactionId",
                table: "duel_matches",
                column: "SettlementTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_duel_matches_Status",
                table: "duel_matches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_duel_matches_WinnerUserId_EndedAt",
                table: "duel_matches",
                columns: new[] { "WinnerUserId", "EndedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_duel_restrictions_OtherUserId_LiftedAt",
                table: "duel_restrictions",
                columns: new[] { "OtherUserId", "LiftedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_duel_restrictions_UserId_LiftedAt",
                table: "duel_restrictions",
                columns: new[] { "UserId", "LiftedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "duel_matches");

            migrationBuilder.DropTable(
                name: "duel_policies");

            migrationBuilder.DropTable(
                name: "duel_restrictions");
        }
    }
}
