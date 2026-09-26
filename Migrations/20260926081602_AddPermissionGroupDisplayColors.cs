using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddPermissionGroupDisplayColors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChatPrimaryColor",
                table: "permission_groups",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ChatSecondaryColor",
                table: "permission_groups",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "NameColor",
                table: "permission_groups",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            // KNG-7: v1's colors, read from its still-live Donator table on 2026-09-26 (PrimaryColor
            // = title/username, SecondColor = the "-{ }-" brackets), stored as "&" formatting codes
            // (YELLOW &e, GOLD &6, AQUA &b, BLUE &9, RED &c, DARK_RED &4, GREEN &a, DARK_GREEN &2,
            // GRAY &7). NameColor is v1's hardcoded scoreboard-team color (Scoreboards/Scoreboard.java),
            // which equals PrimaryColor for the three donator tiers but is GRAY for Default. Plain UPDATEs by Name, the same
            // backfill shape AddUserFeaturesPhase6RealTitleDataAndFreeze used for the tiers'
            // SalaryMultiplier - the rows already exist (Phase 5 / SeedDefaultPermissionGroup), so
            // no permission_holders insert is involved. A group missing on this database is simply
            // skipped.
            migrationBuilder.Sql(@"
UPDATE permission_groups SET ChatPrimaryColor = '&a', ChatSecondaryColor = '&2', NameColor = '&7' WHERE Name = 'Default';
UPDATE permission_groups SET ChatPrimaryColor = '&e', ChatSecondaryColor = '&6', NameColor = '&e' WHERE Name = 'Noble';
UPDATE permission_groups SET ChatPrimaryColor = '&b', ChatSecondaryColor = '&9', NameColor = '&b' WHERE Name = 'Royal';
UPDATE permission_groups SET ChatPrimaryColor = '&c', ChatSecondaryColor = '&4', NameColor = '&c' WHERE Name = 'Dragon Blood';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChatPrimaryColor",
                table: "permission_groups");

            migrationBuilder.DropColumn(
                name: "ChatSecondaryColor",
                table: "permission_groups");

            migrationBuilder.DropColumn(
                name: "NameColor",
                table: "permission_groups");
        }
    }
}
