using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// Landmark domain LD15 (PandiO/knk-workspace#984, DESIGN § 6.1 item 4): the discovery type rule for landmarks,
    /// seeded disabled, so no landmark is discoverable until its per-domain override turns discovery on (LD24). The
    /// rewards are the small structure placeholders, used by a landmark whose override only switches it on; no
    /// ancestors, because a landmark's region may reach outside its parent (LD09). Data only, no schema change.
    /// </summary>
    public partial class AddLandmarkDiscoveryRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "discovery_reward_rules",
                columns: new[] { "DomainType", "IsEnabled", "ExpUnitsMin", "ExpUnitsMax", "CoinSalaryHoursMin", "CoinSalaryHoursMax", "GemsMin", "GemsMax", "IncludeAncestors", "UpdatedAt" },
                values: new object[] { "Landmark", false, 0.05m, 0.25m, 0.05m, 0.25m, 0, 0, false, new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "discovery_reward_rules",
                keyColumn: "DomainType",
                keyValue: "Landmark");
        }
    }
}
