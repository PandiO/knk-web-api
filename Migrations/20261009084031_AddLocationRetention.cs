using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "locations",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "location_orphans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    FlaggedByRunId = table.Column<int>(type: "int", nullable: true),
                    FlaggedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastSeenRunId = table.Column<int>(type: "int", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    World = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    X = table.Column<double>(type: "double", nullable: false),
                    Y = table.Column<double>(type: "double", nullable: false),
                    Z = table.Column<double>(type: "double", nullable: false),
                    Yaw = table.Column<float>(type: "float", nullable: false),
                    Pitch = table.Column<float>(type: "float", nullable: false),
                    LocationCreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DecisionNote = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResolvedReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PreviousItemId = table.Column<int>(type: "int", nullable: true),
                    SupersededByItemId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_location_orphans", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "location_retention_runs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Trigger = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TriggeredByUserId = table.Column<int>(type: "int", nullable: true),
                    ScheduledSlotUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Succeeded = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Error = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CandidatesScanned = table.Column<int>(type: "int", nullable: false),
                    OrphansFound = table.Column<int>(type: "int", nullable: false),
                    NewOrphans = table.Column<int>(type: "int", nullable: false),
                    AlreadyKnown = table.Column<int>(type: "int", nullable: false),
                    Reflagged = table.Column<int>(type: "int", nullable: false),
                    ResolvedCount = table.Column<int>(type: "int", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    DigestQueuedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_location_retention_runs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "location_retention_settings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ScheduleEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Frequency = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    RunDayOfWeek = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    RunAtMinuteOfDay = table.Column<int>(type: "int", nullable: false),
                    GracePeriodDays = table.Column<int>(type: "int", nullable: false),
                    KeptRecheckMonths = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_location_retention_settings", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_location_orphans_LocationId_Status",
                table: "location_orphans",
                columns: new[] { "LocationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_location_orphans_Status_FlaggedAt",
                table: "location_orphans",
                columns: new[] { "Status", "FlaggedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_location_retention_runs_StartedAt",
                table: "location_retention_runs",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_location_retention_runs_Trigger_ScheduledSlotUtc",
                table: "location_retention_runs",
                columns: new[] { "Trigger", "ScheduledSlotUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "location_orphans");

            migrationBuilder.DropTable(
                name: "location_retention_runs");

            migrationBuilder.DropTable(
                name: "location_retention_settings");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "locations");
        }
    }
}
