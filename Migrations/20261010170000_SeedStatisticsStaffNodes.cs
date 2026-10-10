using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using knkwebapi_v2.Properties;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// KNG-34 (developer decision 2026-10-10): grants the player-statistics staff nodes to the staff
    /// groups KNG-80 created (SeedLocationRetentionStaffGroups, which runs first). Group nodes can only be
    /// set through seed data until KNG-62.
    /// Moderator: knk.admin.statistics.view (see every statistic of a player, for moderation).
    /// Admin: knk.admin.statistics.view and knk.admin.privacy.request (file a data-deletion request for a
    /// player without the email step; it still waits the grace period).
    /// The owner-only knk.owner.* nodes are never seeded. A missing group or an existing grant is left
    /// alone. Data only, no schema change.
    /// </summary>
    [DbContext(typeof(KnKDbContext))]
    [Migration("20261010170000_SeedStatisticsStaffNodes")]
    public partial class SeedStatisticsStaffNodes : Migration
    {
        public static readonly string[] ModeratorNodes = { "knk.admin.statistics.view" };

        public static readonly string[] AdminNodes = { "knk.admin.statistics.view", "knk.admin.privacy.request" };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Grant(migrationBuilder, "Moderator", ModeratorNodes);
            Grant(migrationBuilder, "Admin", AdminNodes);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Revoke(migrationBuilder, "Moderator", ModeratorNodes);
            Revoke(migrationBuilder, "Admin", AdminNodes);
        }

        private static void Grant(MigrationBuilder migrationBuilder, string group, IEnumerable<string> nodes)
        {
            foreach (var node in nodes)
            {
                migrationBuilder.Sql(
                    "INSERT INTO permission_grants (HolderId, Node, Value, ExpiresAt) " +
                    $"SELECT pg.Id, '{node}', 1, NULL FROM permission_groups pg WHERE pg.Name = '{group}' " +
                    $"AND NOT EXISTS (SELECT 1 FROM permission_grants g WHERE g.HolderId = pg.Id AND g.Node = '{node}');");
            }
        }

        private static void Revoke(MigrationBuilder migrationBuilder, string group, IEnumerable<string> nodes)
        {
            foreach (var node in nodes)
            {
                migrationBuilder.Sql(
                    "DELETE g FROM permission_grants g INNER JOIN permission_groups pg ON pg.Id = g.HolderId " +
                    $"WHERE pg.Name = '{group}' AND g.Node = '{node}' AND g.Value = 1 AND g.ExpiresAt IS NULL;");
            }
        }
    }
}
