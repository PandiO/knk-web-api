using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddSiegeNavigationItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NavigationItem",
                table: "siege_scenarios",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Inherit",
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "NavigationItem",
                table: "siege_lobbies",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Inherit",
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "GiveNavigationItem",
                table: "siege_configurations",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NavigationItem",
                table: "siege_scenarios");

            migrationBuilder.DropColumn(
                name: "NavigationItem",
                table: "siege_lobbies");

            migrationBuilder.DropColumn(
                name: "GiveNavigationItem",
                table: "siege_configurations");
        }
    }
}
