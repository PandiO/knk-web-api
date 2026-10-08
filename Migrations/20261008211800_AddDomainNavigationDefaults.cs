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

            migrationBuilder.CreateTable(
                name: "domain_navigation_defaults",
                columns: table => new
                {
                    DomainType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DefaultMode = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
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
            // No domain gets an override.
            var seededAt = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);
            migrationBuilder.InsertData(
                table: "domain_navigation_defaults",
                columns: new[] { "DomainType", "DefaultMode", "UpdatedAt" },
                values: new object[,]
                {
                    { "Town", "Spawn", seededAt },
                    { "District", "Spawn", seededAt },
                    { "Structure", "Spawn", seededAt },
                    { "GateStructure", "Spawn", seededAt }
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
        }
    }
}
