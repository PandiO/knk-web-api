using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddGateOpenAnchorAndOpenedBlockSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OpenAnchorPointId",
                table: "gate_structures",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "gate_opened_block_snapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GateStructureId = table.Column<int>(type: "int", nullable: false),
                    RelativeX = table.Column<int>(type: "int", nullable: false),
                    RelativeY = table.Column<int>(type: "int", nullable: false),
                    RelativeZ = table.Column<int>(type: "int", nullable: false),
                    WorldX = table.Column<int>(type: "int", nullable: false),
                    WorldY = table.Column<int>(type: "int", nullable: false),
                    WorldZ = table.Column<int>(type: "int", nullable: false),
                    MaterialName = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BlockDataJson = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TileEntityJson = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gate_opened_block_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gate_opened_block_snapshots_gate_structures_GateStructureId",
                        column: x => x.GateStructureId,
                        principalTable: "gate_structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_gate_structures_OpenAnchorPointId",
                table: "gate_structures",
                column: "OpenAnchorPointId");

            migrationBuilder.CreateIndex(
                name: "IX_GateOpenedBlockSnapshot_GateId_SortOrder",
                table: "gate_opened_block_snapshots",
                columns: new[] { "GateStructureId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_GateOpenedBlockSnapshot_GateStructureId",
                table: "gate_opened_block_snapshots",
                column: "GateStructureId");

            migrationBuilder.CreateIndex(
                name: "IX_GateOpenedBlockSnapshot_WorldCoordinates",
                table: "gate_opened_block_snapshots",
                columns: new[] { "WorldX", "WorldY", "WorldZ" });

            migrationBuilder.AddForeignKey(
                name: "FK_gate_structures_locations_OpenAnchorPointId",
                table: "gate_structures",
                column: "OpenAnchorPointId",
                principalTable: "locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_gate_structures_locations_OpenAnchorPointId",
                table: "gate_structures");

            migrationBuilder.DropTable(
                name: "gate_opened_block_snapshots");

            migrationBuilder.DropIndex(
                name: "IX_gate_structures_OpenAnchorPointId",
                table: "gate_structures");

            migrationBuilder.DropColumn(
                name: "OpenAnchorPointId",
                table: "gate_structures");
        }
    }
}
