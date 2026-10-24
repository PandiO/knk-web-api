using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddSiegeSkipSeconds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AdminSkipMatchmakingSeconds",
                table: "siege_configurations",
                type: "int",
                nullable: false,
                defaultValue: 31);   // the fixed value before the setting existed: T-31

            migrationBuilder.AddColumn<int>(
                name: "PlayerSkipMatchmakingSeconds",
                table: "siege_configurations",
                type: "int",
                nullable: false,
                defaultValue: 60);   // the fixed value before the setting existed: one minute
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminSkipMatchmakingSeconds",
                table: "siege_configurations");

            migrationBuilder.DropColumn(
                name: "PlayerSkipMatchmakingSeconds",
                table: "siege_configurations");
        }
    }
}
