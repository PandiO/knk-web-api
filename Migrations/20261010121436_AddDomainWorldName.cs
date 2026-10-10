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

            // KNG-111 backfill. Existing domains get the world of their own Location first...
            migrationBuilder.Sql(@"
UPDATE `domains` d
JOIN `locations` l ON l.`Id` = d.`LocationId`
SET d.`WorldName` = TRIM(l.`World`)
WHERE d.`WorldName` IS NULL
  AND l.`World` IS NOT NULL
  AND TRIM(l.`World`) <> ''
  AND CHAR_LENGTH(TRIM(l.`World`)) <= 64;");

            // ...then, where that gave nothing, their parent's: Town -> District, then District -> Structure/GateStructure.
            // A domain whose sources disagree or are all missing keeps NULL; the game server's region report
            // (POST api/Domains/world/backfill) or an admin fills it later.
            migrationBuilder.Sql(@"
UPDATE `domains` d
JOIN `districts` ds ON ds.`Id` = d.`Id`
JOIN `domains` t ON t.`Id` = ds.`TownId`
SET d.`WorldName` = t.`WorldName`
WHERE d.`WorldName` IS NULL
  AND t.`WorldName` IS NOT NULL;");

            migrationBuilder.Sql(@"
UPDATE `domains` d
JOIN `structures` st ON st.`Id` = d.`Id`
JOIN `domains` p ON p.`Id` = st.`DistrictId`
SET d.`WorldName` = p.`WorldName`
WHERE d.`WorldName` IS NULL
  AND p.`WorldName` IS NOT NULL;");
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
