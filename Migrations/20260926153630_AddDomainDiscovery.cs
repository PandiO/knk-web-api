using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddDomainDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "discovery_reward_rules",
                columns: table => new
                {
                    DomainType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ExpUnitsMin = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    ExpUnitsMax = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    CoinSalaryHoursMin = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    CoinSalaryHoursMax = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    GemsMin = table.Column<int>(type: "int", nullable: false),
                    GemsMax = table.Column<int>(type: "int", nullable: false),
                    IncludeAncestors = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.DomainType);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "domain_discovery_overrides",
                columns: table => new
                {
                    DomainId = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    ExpUnitsMin = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: true),
                    ExpUnitsMax = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: true),
                    CoinSalaryHoursMin = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: true),
                    CoinSalaryHoursMax = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: true),
                    GemsMin = table.Column<int>(type: "int", nullable: true),
                    GemsMax = table.Column<int>(type: "int", nullable: true),
                    IncludeAncestors = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.DomainId);
                    table.ForeignKey(
                        name: "FK_domain_discovery_overrides_domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "user_domain_discoveries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    DomainId = table.Column<int>(type: "int", nullable: false),
                    DiscoveredAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    Source = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CoinsAwarded = table.Column<int>(type: "int", nullable: false),
                    GemsAwarded = table.Column<int>(type: "int", nullable: false),
                    ExpAwarded = table.Column<int>(type: "int", nullable: false),
                    TitleBracketId = table.Column<int>(type: "int", nullable: true),
                    CoinMultiplier = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    GemMultiplier = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    ExpMultiplier = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_domain_discoveries_domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_domain_discoveries_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_user_domain_discoveries_DomainId",
                table: "user_domain_discoveries",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_user_domain_discoveries_UserId_DiscoveredAt",
                table: "user_domain_discoveries",
                columns: new[] { "UserId", "DiscoveredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_user_domain_discoveries_UserId_DomainId",
                table: "user_domain_discoveries",
                columns: new[] { "UserId", "DomainId" },
                unique: true);

            // One rule per discoverable domain type (docs/specs/domain-discovery/DESIGN.md §3.1,
            // developer decisions 2026-09-26): Town XP units 1-4 and gems 5-15 are v1's values; the
            // rest are placeholders. Structures and gate structures are discoverable with small
            // rewards and no gems. No backfill: nobody starts with discoveries.
            var seededAt = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);
            migrationBuilder.InsertData(
                table: "discovery_reward_rules",
                columns: new[] { "DomainType", "IsEnabled", "ExpUnitsMin", "ExpUnitsMax", "CoinSalaryHoursMin", "CoinSalaryHoursMax", "GemsMin", "GemsMax", "IncludeAncestors", "UpdatedAt" },
                values: new object[,]
                {
                    { "Town", true, 1m, 4m, 2m, 8m, 5, 15, false, seededAt },
                    { "District", true, 0.5m, 2m, 0.5m, 2m, 1, 3, true, seededAt },
                    { "Structure", true, 0.05m, 0.25m, 0.05m, 0.25m, 0, 0, true, seededAt },
                    { "GateStructure", true, 0.05m, 0.25m, 0.05m, 0.25m, 0, 0, true, seededAt }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discovery_reward_rules");

            migrationBuilder.DropTable(
                name: "domain_discovery_overrides");

            migrationBuilder.DropTable(
                name: "user_domain_discoveries");
        }
    }
}
