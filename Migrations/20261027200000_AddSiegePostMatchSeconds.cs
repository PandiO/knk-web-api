using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    // Alpha run 1 (PandiO/knk-workspace#198): the post-match wait at the scenario hub, in seconds.
    public partial class AddSiegePostMatchSeconds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PostMatchSeconds",
                table: "siege_configurations",
                type: "int",
                nullable: false,
                defaultValue: 30);   // the developer's default (2026-10-27)
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PostMatchSeconds",
                table: "siege_configurations");
        }
    }
}
