using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddRoadCuratedTiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CuratedAt",
                table: "road_tiles",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "road_tiles",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Detected",
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "Confirmed",
                table: "road_edges",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "road_tile_proposals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TileId = table.Column<int>(type: "int", nullable: false),
                    BaseVersion = table.Column<int>(type: "int", nullable: false),
                    BuilderVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    CellCount = table.Column<int>(type: "int", nullable: false),
                    LevelCount = table.Column<int>(type: "int", nullable: false),
                    WarningsJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ItemsJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RejectedJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AddedCount = table.Column<int>(type: "int", nullable: false),
                    RemovedCount = table.Column<int>(type: "int", nullable: false),
                    ChangedCount = table.Column<int>(type: "int", nullable: false),
                    MovedCount = table.Column<int>(type: "int", nullable: false),
                    RejectedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_road_tile_proposals_road_tiles_TileId",
                        column: x => x.TileId,
                        principalTable: "road_tiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_road_tile_proposals_TileId",
                table: "road_tile_proposals",
                column: "TileId",
                unique: true);

            // Plan §5.7, D2: built tiles that already hold admin data start Curated, so their next
            // build makes a proposal instead of replacing what the admin fixed. Every other tile
            // starts Detected: its next build is uploaded directly and curates it.
            migrationBuilder.Sql(CurateTilesWithAdminDataSql);
        }

        /// <summary>A tombstone, an anchor or other Manual node, a locked node (names, moves and
        /// merges lock), a designed plaza or a Recorded edge marks a built tile as admin-edited.</summary>
        public const string CurateTilesWithAdminDataSql = @"
UPDATE road_tiles t
SET t.State = 'Curated', t.CuratedAt = UTC_TIMESTAMP()
WHERE t.BuiltAt IS NOT NULL
  AND (EXISTS (SELECT 1 FROM road_nodes n
               WHERE n.TileId = t.Id
                 AND (n.Kind IN ('Pruned', 'PrunedEdge') OR n.Source = 'Manual' OR n.Locked = 1 OR n.PlazaRadius IS NOT NULL))
       OR EXISTS (SELECT 1 FROM road_edges e WHERE e.TileId = t.Id AND e.Source = 'Recorded'));";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "road_tile_proposals");

            migrationBuilder.DropColumn(
                name: "CuratedAt",
                table: "road_tiles");

            migrationBuilder.DropColumn(
                name: "State",
                table: "road_tiles");

            migrationBuilder.DropColumn(
                name: "Confirmed",
                table: "road_edges");
        }
    }
}
