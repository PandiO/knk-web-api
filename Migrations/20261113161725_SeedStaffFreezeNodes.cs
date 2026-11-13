using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// Staff may freeze and unfreeze players (PandiO/knk-workspace#367, developer decision 2026-11-13: Staff helps
    /// players and enforces the rules, so freeze, kick, ban and balances, but doesn't manage the game core). Gives the
    /// <b>Staff</b> group <c>knk.freeze</c> and <c>knk.unfreeze</c>, the nodes the API's <c>PUT api/Users/{id}/freeze</c>
    /// and <c>/unfreeze</c> and the plugin's <c>/freeze</c> and <c>/unfreeze</c> check. Data only, no schema change.
    ///
    /// <para>Idempotent: the group is found by name as in <see cref="SeedDashboardActionNodes"/>, and a node is skipped
    /// when the group is missing (a database made by migrations only has no Staff group) or when Staff or a group it
    /// inherits from already has a grant or deny of the node or a wildcard covering it
    /// (<see cref="SeedDashboardActionNodes.Decides"/>). On the alpha and the dev seed Staff already holds both nodes, so
    /// nothing is added there; a deny is never overridden.</para>
    ///
    /// <para>Down removes nothing: a grant this Up added can't be told apart from the same grant given by hand or by the
    /// dev seed before (there is no marker column), and taking Staff's freeze away on a rollback would be the worse
    /// mistake. A grant left behind by a rollback is removed on the group's page in the web app.</para>
    /// </summary>
    public partial class SeedStaffFreezeNodes : Migration
    {
        public const string Freeze = "knk.freeze";
        public const string Unfreeze = "knk.unfreeze";

        /// <summary>The group that gets the nodes (only this one; the ranks above it inherit from it on the alpha).</summary>
        public const string StaffGroup = "Staff";

        /// <summary>The nodes Staff gets.</summary>
        public static readonly string[] Nodes = { Freeze, Unfreeze };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var node in Nodes)
            {
                migrationBuilder.Sql(GrantSql(StaffGroup, node));
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty, see the class summary.
        }

        /// <summary>
        /// Grants <paramref name="node"/> to <paramref name="group"/> unless the group or a group in its parent chain
        /// already has a grant or deny that decides it.
        /// </summary>
        public static string GrantSql(string group, string node) =>
            "INSERT INTO permission_grants (HolderId, Node, Value, ExpiresAt) " +
            "WITH RECURSIVE anc (GroupId, AncestorId, Depth) AS (" +
            "SELECT Id, Id, 0 FROM permission_groups " +
            "UNION ALL SELECT a.GroupId, p.ParentGroupId, a.Depth + 1 FROM anc a " +
            "INNER JOIN permission_groups p ON p.Id = a.AncestorId WHERE p.ParentGroupId IS NOT NULL AND a.Depth < 32) " +
            $"SELECT pg.Id, '{node}', 1, NULL FROM permission_groups pg WHERE pg.Name = '{group}' " +
            "AND NOT EXISTS (SELECT 1 FROM anc INNER JOIN permission_grants g ON g.HolderId = anc.AncestorId " +
            $"WHERE anc.GroupId = pg.Id AND {SeedDashboardActionNodes.Decides(node)});";
    }
}
