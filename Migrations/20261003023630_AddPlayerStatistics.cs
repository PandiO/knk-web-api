using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerStatistics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "player_pvp_kill_pairs_daily",
                columns: table => new
                {
                    KillerUserId = table.Column<int>(type: "int", nullable: false),
                    VictimUserId = table.Column<int>(type: "int", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    ContextKey = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.KillerUserId, x.VictimUserId, x.Day, x.ContextKey });
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "player_stat_batches",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ServerName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReceivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EntryCount = table.Column<int>(type: "int", nullable: false),
                    RejectedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.BatchId);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "player_stat_daily",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    MetricKey = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContextKey = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<decimal>(type: "decimal(20,4)", precision: 20, scale: 4, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.UserId, x.Day, x.MetricKey, x.ContextKey });
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "player_stat_profiles",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    FirstSessionAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LeaderboardExcluded = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LeaderboardExcludedReason = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeaderboardExcludedByUserId = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.UserId);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "player_stat_sessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SessionKey = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastHeartbeatAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EndReason = table.Column<byte>(type: "tinyint unsigned", nullable: true),
                    ActiveSeconds = table.Column<int>(type: "int", nullable: false),
                    AfkSeconds = table.Column<int>(type: "int", nullable: false),
                    ServerName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "player_stat_totals",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    MetricKey = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContextKey = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<decimal>(type: "decimal(20,4)", precision: 20, scale: 4, nullable: false),
                    ReachedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.UserId, x.MetricKey, x.ContextKey });
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "player_stat_visibility",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SettingKey = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContextKey = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Visibility = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.UserId, x.SettingKey, x.ContextKey });
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "player_title_changes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    FromTitleBracketId = table.Column<int>(type: "int", nullable: true),
                    ToTitleBracketId = table.Column<int>(type: "int", nullable: false),
                    FromTitleName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ToTitleName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Direction = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ExperienceBefore = table.Column<long>(type: "bigint", nullable: false),
                    ExperienceAfter = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyEntryId = table.Column<long>(type: "bigint", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "statistics_projected_sources",
                columns: table => new
                {
                    SourceType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceId = table.Column<long>(type: "bigint", nullable: false),
                    ProjectedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.SourceType, x.SourceId });
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "statistics_projection_cursors",
                columns: table => new
                {
                    Name = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastSourceId = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Name);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_player_pvp_kill_pairs_daily_Day",
                table: "player_pvp_kill_pairs_daily",
                column: "Day");

            migrationBuilder.CreateIndex(
                name: "IX_player_stat_batches_ReceivedAt",
                table: "player_stat_batches",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_player_stat_daily_metric_day",
                table: "player_stat_daily",
                columns: new[] { "MetricKey", "ContextKey", "Day" });

            migrationBuilder.CreateIndex(
                name: "IX_player_stat_sessions_EndedAt_LastHeartbeatAt",
                table: "player_stat_sessions",
                columns: new[] { "EndedAt", "LastHeartbeatAt" });

            migrationBuilder.CreateIndex(
                name: "IX_player_stat_sessions_SessionKey",
                table: "player_stat_sessions",
                column: "SessionKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_player_stat_sessions_UserId_StartedAt",
                table: "player_stat_sessions",
                columns: new[] { "UserId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_player_stat_totals_MetricKey_ContextKey_Value",
                table: "player_stat_totals",
                columns: new[] { "MetricKey", "ContextKey", "Value" });

            migrationBuilder.CreateIndex(
                name: "IX_player_stat_visibility_SettingKey_ContextKey_Visibility",
                table: "player_stat_visibility",
                columns: new[] { "SettingKey", "ContextKey", "Visibility" });

            migrationBuilder.CreateIndex(
                name: "IX_player_title_changes_CurrencyEntryId",
                table: "player_title_changes",
                column: "CurrencyEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_player_title_changes_UserId_ChangedAt",
                table: "player_title_changes",
                columns: new[] { "UserId", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "player_pvp_kill_pairs_daily");

            migrationBuilder.DropTable(
                name: "player_stat_batches");

            migrationBuilder.DropTable(
                name: "player_stat_daily");

            migrationBuilder.DropTable(
                name: "player_stat_profiles");

            migrationBuilder.DropTable(
                name: "player_stat_sessions");

            migrationBuilder.DropTable(
                name: "player_stat_totals");

            migrationBuilder.DropTable(
                name: "player_stat_visibility");

            migrationBuilder.DropTable(
                name: "player_title_changes");

            migrationBuilder.DropTable(
                name: "statistics_projected_sources");

            migrationBuilder.DropTable(
                name: "statistics_projection_cursors");
        }
    }
}
