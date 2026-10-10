using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// KNG-80: seeds the Location retention nodes into staff groups. Trunk had no staff groups (only
    /// Default and the premium tiers), and group nodes can only be set through seed data until KNG-62,
    /// so this creates "Moderator" (weight 50) and "Admin" (weight 100) when they don't exist yet
    /// (developer decision 2026-10-09) - above the premium tiers (10-30) - and grants:
    /// Moderator: view, notify, keep, tp. Admin: all seven, including delete, run and settings.
    /// Existing groups of those names are reused, and a grant already present is left alone.
    /// </summary>
    public partial class SeedLocationRetentionStaffGroups : Migration
    {
        private static readonly (string Group, int Weight)[] Groups = { ("Moderator", 50), ("Admin", 100) };

        private static readonly string[] ModeratorNodes =
        {
            "knk.admin.location.orphans",
            "knk.admin.location.orphans.notify",
            "knk.admin.location.orphans.keep",
            "knk.admin.location.tp"
        };

        private static readonly string[] AdminNodes =
        {
            "knk.admin.location.orphans",
            "knk.admin.location.orphans.notify",
            "knk.admin.location.orphans.keep",
            "knk.admin.location.orphans.delete",
            "knk.admin.location.orphans.run",
            "knk.admin.location.retention",
            "knk.admin.location.tp"
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (group, weight) in Groups)
            {
                // TPT: a permission_holders row, then the permission_groups row with its Id, as in
                // SeedDefaultPermissionGroup. Both inserts are skipped when the group exists, so a
                // stale LAST_INSERT_ID() is never used.
                migrationBuilder.Sql(
                    "INSERT INTO permission_holders (ChatPrefix, ChatSuffix) " +
                    $"SELECT NULL, NULL FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM permission_groups WHERE Name = '{group}');");
                migrationBuilder.Sql(
                    "INSERT INTO permission_groups (Id, Name, Weight, IsPremiumTier, SalaryMultiplier, ParentGroupId) " +
                    $"SELECT LAST_INSERT_ID(), '{group}', {weight}, 0, 1.0, NULL FROM DUAL " +
                    $"WHERE NOT EXISTS (SELECT 1 FROM permission_groups WHERE Name = '{group}');");
            }

            Grant(migrationBuilder, "Moderator", ModeratorNodes);
            Grant(migrationBuilder, "Admin", AdminNodes);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Revoke(migrationBuilder, "Moderator", ModeratorNodes);
            Revoke(migrationBuilder, "Admin", AdminNodes);
            // Only a group left exactly as this migration made it (no members, no other grants) is removed.
            foreach (var (group, _) in Groups)
            {
                migrationBuilder.Sql(
                    "DELETE ph FROM permission_holders ph INNER JOIN permission_groups pg ON pg.Id = ph.Id " +
                    $"WHERE pg.Name = '{group}' AND pg.ParentGroupId IS NULL " +
                    "AND NOT EXISTS (SELECT 1 FROM user_permission_groups m WHERE m.PermissionGroupId = pg.Id) " +
                    "AND NOT EXISTS (SELECT 1 FROM permission_grants g WHERE g.HolderId = pg.Id);");
            }
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
