using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaderboards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "leaderboard_snapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BoardKey = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Period = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: true),
                    GeneratedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsCurrent = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EntryCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "leaderboard_snapshot_entries",
                columns: table => new
                {
                    SnapshotId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(20,4)", precision: 20, scale: 4, nullable: false),
                    ReachedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.SnapshotId, x.UserId });
                    table.ForeignKey(
                        name: "FK_leaderboard_snapshot_entries_leaderboard_snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "leaderboard_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_leaderboard_snapshot_entries_SnapshotId_Rank",
                table: "leaderboard_snapshot_entries",
                columns: new[] { "SnapshotId", "Rank" });

            migrationBuilder.CreateIndex(
                name: "IX_leaderboard_snapshots_BoardKey_Period_IsCurrent",
                table: "leaderboard_snapshots",
                columns: new[] { "BoardKey", "Period", "IsCurrent" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leaderboard_snapshot_entries");

            migrationBuilder.DropTable(
                name: "leaderboard_snapshots");
        }
    }
}
