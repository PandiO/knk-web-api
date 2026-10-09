using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddDomainNavigationDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NavigationDefaultOverride",
                table: "domains",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            // Rev. 7 Part C (KNG-92): null follows the type's RoadAccess.
            migrationBuilder.AddColumn<string>(
                name: "RoadAccessOverride",
                table: "domains",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "domain_navigation_defaults",
                columns: table => new
                {
                    DomainType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DefaultMode = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RoadAccess = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.DomainType);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            // KNG-73: every type starts at Spawn, which is what /navigate <domain> did before.
            // Rev. 7 Part C (KNG-92, decision D3): every current type keeps its entry rule on the roads
            // (Applies), which is what the router did before; houses, shops and the like will start at Ignored.
            // No domain gets an override.
            var seededAt = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);
            migrationBuilder.InsertData(
                table: "domain_navigation_defaults",
                columns: new[] { "DomainType", "DefaultMode", "RoadAccess", "UpdatedAt" },
                values: new object[,]
                {
                    { "Town", "Spawn", "Applies", seededAt },
                    { "District", "Spawn", "Applies", seededAt },
                    { "Structure", "Spawn", "Applies", seededAt },
                    { "GateStructure", "Spawn", "Applies", seededAt }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "domain_navigation_defaults");

            migrationBuilder.DropColumn(
                name: "NavigationDefaultOverride",
                table: "domains");

            migrationBuilder.DropColumn(
                name: "RoadAccessOverride",
                table: "domains");
        }
    }
}
