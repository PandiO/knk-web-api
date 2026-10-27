using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using knkwebapi_v2.Properties;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// Closed-alpha test run 1, package I: rank seeds for the plugin's new nodes, the same way as
    /// <see cref="SeedCommandPermissionNodes"/> (its tiers and its <see cref="SeedCommandPermissionNodes.GrantSql"/>: a
    /// group is skipped when it is missing, or when it or a group in its parent chain already has a grant or deny for
    /// the node or a wildcard covering it).
    /// <list type="bullet">
    /// <item><c>knk.teleport.all.hub|spawn|tp|tphere</c> (PandiO/knk-workspace#171, <c>/hub all</c>, <c>/spawn all</c>,
    /// <c>/tp all &lt;player&gt;</c>, <c>/tphere all</c>): Moderator and Admin, not the general Staff rank
    /// (conservative default, for review).</item>
    /// <item><c>knk.admin.item.more</c> (#175, <c>/knk item more</c>): Admin (on the alpha already covered by
    /// <c>knk.admin.*</c>, so nothing is added there).</item>
    /// </list>
    /// The exemption nodes (<c>knk.teleport.exempt.*</c>) get no seed: who is exempt is the developer's call; staff and
    /// owner mode already exempt. Down removes only these grants (value 1, no expiry). Data only, no schema change.
    /// </summary>
    [DbContext(typeof(KnKDbContext))]
    [Migration("20261027200000_SeedTeleportAllAndItemMoreNodes")]
    public partial class SeedTeleportAllAndItemMoreNodes : Migration
    {
        /// <summary>Node → tier (<see cref="SeedCommandPermissionNodes.Tiers"/>).</summary>
        public static readonly (string Node, string Tier)[] NewGrants =
        {
            ("knk.teleport.all.hub", "Moderator"),
            ("knk.teleport.all.spawn", "Moderator"),
            ("knk.teleport.all.tp", "Moderator"),
            ("knk.teleport.all.tphere", "Moderator"),
            ("knk.admin.item.more", "Admin"),
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (node, tier) in NewGrants)
            {
                foreach (var group in SeedCommandPermissionNodes.Tiers[tier])
                {
                    migrationBuilder.Sql(SeedCommandPermissionNodes.GrantSql(group, node));
                }
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (node, tier) in NewGrants)
            {
                foreach (var group in SeedCommandPermissionNodes.Tiers[tier])
                {
                    migrationBuilder.Sql(
                        "DELETE g FROM permission_grants g INNER JOIN permission_groups pg ON pg.Id = g.HolderId " +
                        $"WHERE pg.Name = '{group}' AND g.Node = '{node}' AND g.Value = 1 AND g.ExpiresAt IS NULL;");
                }
            }
        }
    }
}
