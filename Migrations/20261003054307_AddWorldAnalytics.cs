using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddWorldAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "domain_interactions_daily",
                columns: table => new
                {
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    DomainId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Count = table.Column<int>(type: "int", nullable: false),
                    UniquePlayers = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.Day, x.DomainId, x.Kind });
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "menu_funnel_daily",
                columns: table => new
                {
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    MenuKey = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Step = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Outcome = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.Day, x.MenuKey, x.Step, x.Outcome });
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "world_analytics_batches",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ServerName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReceivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    WindowStart = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    RejectedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.BatchId);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "world_movement_cells_daily",
                columns: table => new
                {
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    World = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CellSize = table.Column<short>(type: "smallint", nullable: false),
                    CellX = table.Column<int>(type: "int", nullable: false),
                    CellZ = table.Column<int>(type: "int", nullable: false),
                    Samples = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => new { x.Day, x.World, x.CellSize, x.CellX, x.CellZ });
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_domain_interactions_daily_domain_day",
                table: "domain_interactions_daily",
                columns: new[] { "DomainId", "Day" });

            migrationBuilder.CreateIndex(
                name: "IX_menu_funnel_daily_menu_day",
                table: "menu_funnel_daily",
                columns: new[] { "MenuKey", "Day" });

            migrationBuilder.CreateIndex(
                name: "IX_world_analytics_batches_ReceivedAt",
                table: "world_analytics_batches",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_world_movement_cells_daily_world_day",
                table: "world_movement_cells_daily",
                columns: new[] { "World", "Day" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "domain_interactions_daily");

            migrationBuilder.DropTable(
                name: "menu_funnel_daily");

            migrationBuilder.DropTable(
                name: "world_analytics_batches");

            migrationBuilder.DropTable(
                name: "world_movement_cells_daily");
        }
    }
}
