using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddGameSettingsMotdAndGroupOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GroupOverridesJson",
                table: "game_settings",
                type: "longtext",
                nullable: false,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Motd",
                table: "game_settings",
                type: "longtext",
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GroupOverridesJson",
                table: "game_settings");

            migrationBuilder.DropColumn(
                name: "Motd",
                table: "game_settings");
        }
    }
}
