using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class DropSiegeWeaponSpotBounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxX",
                table: "siege_weapon_spots");

            migrationBuilder.DropColumn(
                name: "MaxY",
                table: "siege_weapon_spots");

            migrationBuilder.DropColumn(
                name: "MaxZ",
                table: "siege_weapon_spots");

            migrationBuilder.DropColumn(
                name: "MinX",
                table: "siege_weapon_spots");

            migrationBuilder.DropColumn(
                name: "MinY",
                table: "siege_weapon_spots");

            migrationBuilder.DropColumn(
                name: "MinZ",
                table: "siege_weapon_spots");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxX",
                table: "siege_weapon_spots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxY",
                table: "siege_weapon_spots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxZ",
                table: "siege_weapon_spots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinX",
                table: "siege_weapon_spots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinY",
                table: "siege_weapon_spots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinZ",
                table: "siege_weapon_spots",
                type: "int",
                nullable: true);
        }
    }
}
