using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// PandiO/knk-workspace#409: a town's streets are the distinct streets of its districts (<c>DistrictStreet</c>), so the
    /// separate <c>TownStreet</c> link table goes, with every row (developer decision 2026-11-14 on #515; the backup taken
    /// before the migration is the way back for rows no district covered). <c>Down</c> recreates the table as
    /// <c>InitialCreate</c> made it and refills it from the districts, so rolled-back code shows what this code showed.
    /// </summary>
    public partial class DropTownStreet : Migration
    {
        /// <summary>Down: one row per distinct (street, town) pair of the districts' streets.</summary>
        public const string RefillFromDistrictsSql =
            "INSERT INTO `TownStreet` (`StreetId`, `TownId`) " +
            "SELECT DISTINCT ds.`StreetId`, d.`TownId` FROM `DistrictStreet` ds JOIN `districts` d ON d.`Id` = ds.`DistrictId`;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TownStreet");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TownStreet",
                columns: table => new
                {
                    StreetId = table.Column<int>(type: "int", nullable: false),
                    TownId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TownStreet", x => new { x.StreetId, x.TownId });
                    table.ForeignKey(
                        name: "FK_TownStreet_streets_StreetId",
                        column: x => x.StreetId,
                        principalTable: "streets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TownStreet_towns_TownId",
                        column: x => x.TownId,
                        principalTable: "towns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_TownStreet_TownId",
                table: "TownStreet",
                column: "TownId");

            migrationBuilder.Sql(RefillFromDistrictsSql);
        }
    }
}
