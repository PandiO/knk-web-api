using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class RenameGateDoorRegionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RegionOpenedId",
                table: "gate_doors",
                newName: "OpenedRegionData");

            migrationBuilder.RenameColumn(
                name: "RegionClosedId",
                table: "gate_doors",
                newName: "ClosedRegionData");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OpenedRegionData",
                table: "gate_doors",
                newName: "RegionOpenedId");

            migrationBuilder.RenameColumn(
                name: "ClosedRegionData",
                table: "gate_doors",
                newName: "RegionClosedId");
        }
    }
}
