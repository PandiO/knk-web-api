using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddDomainWorldName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WorldName",
                table: "domains",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            // KNG-111: no backfill here. Older Locations often carry a defaulted "world" that need not match the
            // server's real world names, and a wrong world would hide a domain from world-qualified lookups. Rows stay
            // NULL - which every world-qualified lookup still matches - until the game server reports where each
            // region really is (POST api/Domains/world/backfill, sent at plugin startup) or an admin sets the world.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorldName",
                table: "domains");
        }
    }
}
