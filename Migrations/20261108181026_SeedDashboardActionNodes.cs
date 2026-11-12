using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// Dashboard access model, link AM1 (PandiO/knk-workspace#280, knk-workspace docs/specs/dashboard-access-model/DESIGN.md
    /// § 4.5): the new action nodes go to the holders who could already do the action. Data only, no schema change.
    /// <list type="number">
    ///   <item><b>Admin</b> gets <c>knk.admin.delete</c>, <c>knk.admin.delete.bulk</c> and <c>knk.admin.export</c>, each
    ///     skipped when Admin or a group it inherits from already has a grant or deny of the node or a wildcard covering
    ///     it. On the alpha (Admin holds <c>knk.admin.*</c>) nothing is added.</item>
    ///   <item><b>Backfill</b> (decision 3): every group and user with an active, explicit grant of an area node
    ///     (<see cref="AreaNodes"/>) gets <c>knk.admin.delete</c> and <c>knk.admin.export</c>, so nobody who deletes
    ///     today loses it, <b>except the Staff group</b> (developer, 2026-11-08: Staff keeps balance changes, freeze,
    ///     kick and ban, but gets no delete). A holder is skipped when it, a group it inherits from or (for a user) one
    ///     of its active groups and their parents already has a grant or deny of the node or a covering wildcard, so a
    ///     deny is never overridden. <c>knk.admin.delete.bulk</c> is never backfilled: it is new, and only the owner
    ///     hands it out.</item>
    /// </list>
    /// <para>The area nodes and node names are written out here rather than read from <c>DashboardAccess</c>, so a later
    /// change to the registry doesn't change what this migration did. Down removes only value-1 grants without expiry
    /// of the new nodes on the holders Up could have touched; like the earlier seed migrations it can't tell its own
    /// grant from an identical one added by hand later, and it never touches a deny.</para>
    /// </summary>
    public partial class SeedDashboardActionNodes : Migration
    {
        public const string Delete = "knk.admin.delete";
        public const string DeleteBulk = "knk.admin.delete.bulk";
        public const string Export = "knk.admin.export";

        /// <summary>The group that holds all three; on a database made by migrations only it has no wildcard.</summary>
        public const string AdminGroup = "Admin";

        /// <summary>Left out of the backfill (developer decision of 2026-11-08 on #280).</summary>
        public const string ExcludedGroup = "Staff";

        /// <summary>The nodes Admin gets.</summary>
        public static readonly string[] AdminNodes = { Delete, DeleteBulk, Export };

        /// <summary>The nodes every explicit area-node holder gets.</summary>
        public static readonly string[] BackfillNodes = { Delete, Export };

        /// <summary>The area nodes of DESIGN.md § 4.1 (view, create and edit per area).</summary>
        public static readonly string[] AreaNodes =
        {
            "knk.admin.content", "knk.admin.world", "knk.kit.manage", "knk.admin.lootbox.manage",
            "knk.siege.admin.manage", "knk.admin.road", "knk.admin.user.manage", "knk.admin.user.perm",
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var node in AdminNodes)
            {
                migrationBuilder.Sql(AdminGrantSql(node));
            }
            foreach (var node in BackfillNodes)
            {
                migrationBuilder.Sql(GroupBackfillSql(node));
                migrationBuilder.Sql(UserBackfillSql(node));
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var node in BackfillNodes)
            {
                migrationBuilder.Sql(
                    "DELETE FROM permission_grants WHERE Node = '" + node + "' AND Value = 1 AND ExpiresAt IS NULL " +
                    "AND HolderId IN (SELECT HolderId FROM (SELECT DISTINCT e.HolderId FROM permission_grants e " +
                    $"WHERE e.Value = 1 AND e.Node IN ({AreaNodeList}) " +
                    $"AND NOT EXISTS (SELECT 1 FROM permission_groups s WHERE s.Id = e.HolderId AND s.Name = '{ExcludedGroup}')) holders);");
            }
            foreach (var node in AdminNodes)
            {
                migrationBuilder.Sql(
                    "DELETE g FROM permission_grants g INNER JOIN permission_groups pg ON pg.Id = g.HolderId " +
                    $"WHERE pg.Name = '{AdminGroup}' AND g.Node = '{node}' AND g.Value = 1 AND g.ExpiresAt IS NULL;");
            }
        }

        private static string AreaNodeList => string.Join(", ", AreaNodes.Select(n => $"'{n}'"));

        /// <summary>
        /// The parent chain of every group: (GroupId, AncestorId) for the group itself and each group above it, as in
        /// SeedCommandPermissionNodes.GrantSql.
        /// </summary>
        private const string AncestorsCte =
            "anc (GroupId, AncestorId, Depth) AS (" +
            "SELECT Id, Id, 0 FROM permission_groups " +
            "UNION ALL SELECT a.GroupId, p.ParentGroupId, a.Depth + 1 FROM anc a " +
            "INNER JOIN permission_groups p ON p.Id = a.AncestorId WHERE p.ParentGroupId IS NOT NULL AND a.Depth < 32)";

        /// <summary>
        /// A grant or deny <c>g</c> that decides <paramref name="node"/>: the node itself, or a wildcard covering it as
        /// PermissionResolutionService.TryMatch resolves it — a trailing <c>*</c> matches every node starting with what
        /// comes before it, and <c>x.*</c> also the bare node <c>x</c> (so a deny of <c>knk.admin.delete.*</c> is kept
        /// for <c>knk.admin.delete</c>).
        /// </summary>
        public static string Decides(string node) =>
            $"(g.Node = '{node}' OR (g.Node LIKE '%*' " +
            $"AND '{node}' LIKE CONCAT(REPLACE(REPLACE(LEFT(g.Node, CHAR_LENGTH(g.Node) - 1), '_', '\\\\_'), '%', '\\\\%'), '%')) " +
            $"OR (g.Node LIKE '%.*' AND LEFT(g.Node, CHAR_LENGTH(g.Node) - 2) = '{node}'))";

        /// <summary>Holder <paramref name="holder"/> has an active, explicit (exact, value 1) grant of an area node.</summary>
        private static string HoldsAreaNode(string holder) =>
            $"EXISTS (SELECT 1 FROM permission_grants e WHERE e.HolderId = {holder} AND e.Value = 1 " +
            $"AND (e.ExpiresAt IS NULL OR e.ExpiresAt > UTC_TIMESTAMP()) AND e.Node IN ({AreaNodeList}))";

        public static string AdminGrantSql(string node) =>
            "INSERT INTO permission_grants (HolderId, Node, Value, ExpiresAt) " +
            $"WITH RECURSIVE {AncestorsCte} " +
            $"SELECT pg.Id, '{node}', 1, NULL FROM permission_groups pg WHERE pg.Name = '{AdminGroup}' " +
            "AND NOT EXISTS (SELECT 1 FROM anc INNER JOIN permission_grants g ON g.HolderId = anc.AncestorId " +
            $"WHERE anc.GroupId = pg.Id AND {Decides(node)});";

        public static string GroupBackfillSql(string node) =>
            "INSERT INTO permission_grants (HolderId, Node, Value, ExpiresAt) " +
            $"WITH RECURSIVE {AncestorsCte} " +
            $"SELECT pg.Id, '{node}', 1, NULL FROM permission_groups pg WHERE pg.Name <> '{ExcludedGroup}' " +
            $"AND {HoldsAreaNode("pg.Id")} " +
            "AND NOT EXISTS (SELECT 1 FROM anc INNER JOIN permission_grants g ON g.HolderId = anc.AncestorId " +
            $"WHERE anc.GroupId = pg.Id AND {Decides(node)});";

        /// <summary>
        /// Users with their own explicit area grant. The chain checked is the user's own grants and every active group
        /// they belong to with its parents, as PermissionResolutionService builds it.
        /// </summary>
        public static string UserBackfillSql(string node) =>
            "INSERT INTO permission_grants (HolderId, Node, Value, ExpiresAt) " +
            $"WITH RECURSIVE {AncestorsCte}, " +
            "chain (UserId, HolderId) AS (" +
            "SELECT u.Id, u.Id FROM users u " +
            "UNION SELECT m.UserId, anc.AncestorId FROM user_permission_groups m INNER JOIN anc ON anc.GroupId = m.PermissionGroupId " +
            "WHERE m.ExpiresAt IS NULL OR m.ExpiresAt > UTC_TIMESTAMP()) " +
            $"SELECT u.Id, '{node}', 1, NULL FROM users u WHERE {HoldsAreaNode("u.Id")} " +
            "AND NOT EXISTS (SELECT 1 FROM chain c INNER JOIN permission_grants g ON g.HolderId = c.HolderId " +
            $"WHERE c.UserId = u.Id AND {Decides(node)});";
    }
}
