using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using knkwebapi_v2.Properties;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// Closed-alpha test run 1, package C (PandiO/knk-workspace#158 command permission scan, #157 container bypass,
    /// #159 join-refusal notices): gives the plugin's command nodes that no rank held to the ranks meant to use them.
    /// Group nodes can only be set through seed data until KNG-62, as in SeedLocationRetentionStaffGroups.
    ///
    /// <para>Each node goes to a tier, the lowest group meant to hold it, and to the staff groups above it:
    /// Staff → Staff, Moderator, Admin; Moderator → Moderator, Admin; Admin → Admin. A group is skipped when it
    /// doesn't exist, or when it or a group it inherits from (ParentGroupId chain) already has a grant or deny for
    /// the node, or a wildcard grant covering it (<c>*</c>, <c>knk.*</c>, <c>knk.admin.*</c>, ...). So on the alpha
    /// database (Staff → Moderator → Admin inherit from each other, Admin holds <c>knk.admin.*</c>) a node lands
    /// once, on its tier's group, and on a database made by migrations only (Moderator and Admin without parents, no
    /// Staff group) each staff group gets it. An existing grant or deny is never changed.</para>
    ///
    /// <para><see cref="BackfillGrants"/> are the <c>knk.siege.*</c> nodes the alpha seed already gives by hand
    /// (none had a seed here, see the comment on #158); their Down leaves them, because on the alpha they predate
    /// this migration. Down removes only the <see cref="NewGrants"/> it could have added (value 1, no expiry).
    /// Data only, no schema change.</para>
    /// </summary>
    [DbContext(typeof(KnKDbContext))]
    [Migration("20261026160000_SeedCommandPermissionNodes")]
    public partial class SeedCommandPermissionNodes : Migration
    {
        /// <summary>The groups a tier's node goes to, lowest first (ancestors are seeded before their children).</summary>
        public static readonly IReadOnlyDictionary<string, string[]> Tiers = new Dictionary<string, string[]>
        {
            ["Default"] = new[] { "Default" },
            ["Noble"] = new[] { "Noble" },
            ["Staff"] = new[] { "Staff", "Moderator", "Admin" },
            ["Moderator"] = new[] { "Moderator", "Admin" },
            ["Admin"] = new[] { "Admin" },
        };

        /// <summary>Node → tier for the nodes this package adds or gives to a rank for the first time.</summary>
        public static readonly (string Node, string Tier)[] NewGrants =
        {
            // #158: world/item/kit scan tasks (/knk tasks, task-claim, task-status, itemscan, kitscan)
            ("knk.tasks", "Staff"),
            // #158: /knk item (own node now) and /knk itemblueprints (list/search/give) - "KnK items"
            ("knk.admin.item", "Staff"),
            ("knk.admin.itemblueprints", "Staff"),
            // #158: /gamemode (new KnK command; vanilla /gamemode is op-only)
            ("knk.gamemode.survival", "Staff"),
            ("knk.gamemode.spectator", "Staff"),
            ("knk.gamemode.others", "Moderator"),
            ("knk.gamemode.*", "Admin"),
            // #158: player utilities nobody held
            ("knk.fly", "Staff"),
            ("knk.fly.others", "Moderator"),
            ("knk.enderchest.open", "Moderator"),
            ("knk.inventory.open", "Moderator"),
            ("knk.inventory.clear", "Admin"),
            ("knk.hub.bypass", "Admin"),
            // #158 comment: /siege join <player> (package A) goes to admins, not the general staff rank
            ("knk.siege.admin.join", "Admin"),
            // #157: storage in domains; moderators and admins, not the general staff rank (developer, 2026-10-25)
            ("knk.region.bypass.containers", "Moderator"),
            // #159: refused-join notices
            ("knk.notify.joinattempt", "Staff"),
        };

        /// <summary>Siege nodes the alpha seed gives by hand; seeded where missing, never removed by Down.</summary>
        public static readonly (string Node, string Tier)[] BackfillGrants =
        {
            ("knk.siege.play", "Default"),
            ("knk.siege.skip", "Noble"),
            ("knk.siege.admin.list", "Staff"),
            ("knk.siege.admin.control", "Staff"),
            ("knk.siege.admin.reload", "Staff"),
            ("knk.siege.admin.manage", "Staff"),
            ("knk.siege.bypass.commands", "Staff"),
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (node, tier) in BackfillGrants.Concat(NewGrants))
            {
                foreach (var group in Tiers[tier])
                {
                    migrationBuilder.Sql(GrantSql(group, node));
                }
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (node, tier) in NewGrants)
            {
                foreach (var group in Tiers[tier])
                {
                    migrationBuilder.Sql(
                        "DELETE g FROM permission_grants g INNER JOIN permission_groups pg ON pg.Id = g.HolderId " +
                        $"WHERE pg.Name = '{group}' AND g.Node = '{node}' AND g.Value = 1 AND g.ExpiresAt IS NULL;");
                }
            }
        }

        /// <summary>
        /// Grants <paramref name="node"/> to <paramref name="group"/> unless the group or an ancestor has a grant or
        /// deny for it, or a wildcard grant covering it (a trailing <c>*</c> matches every node starting with what
        /// comes before it, as PermissionResolutionService resolves it).
        /// </summary>
        public static string GrantSql(string group, string node) =>
            "INSERT INTO permission_grants (HolderId, Node, Value, ExpiresAt) " +
            "WITH RECURSIVE anc (GroupId, AncestorId, Depth) AS (" +
            "SELECT Id, Id, 0 FROM permission_groups " +
            "UNION ALL SELECT a.GroupId, p.ParentGroupId, a.Depth + 1 FROM anc a " +
            "INNER JOIN permission_groups p ON p.Id = a.AncestorId WHERE p.ParentGroupId IS NOT NULL AND a.Depth < 32) " +
            $"SELECT pg.Id, '{node}', 1, NULL FROM permission_groups pg WHERE pg.Name = '{group}' " +
            "AND NOT EXISTS (SELECT 1 FROM anc INNER JOIN permission_grants g ON g.HolderId = anc.AncestorId " +
            $"WHERE anc.GroupId = pg.Id AND (g.Node = '{node}' OR (g.Node LIKE '%*' " +
            $"AND '{node}' LIKE CONCAT(REPLACE(REPLACE(LEFT(g.Node, CHAR_LENGTH(g.Node) - 1), '_', '\\\\_'), '%', '\\\\%'), '%'))));";
    }
}
