using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// KNG-218 (docs/specs/invitations/IMPLEMENTATION_PLAN.md § 8): seeds the "Alpha Tester" group that
    /// Invitations:InvitableGroups names by default, so invitations can grant it (decision I4). Weight 1 (above
    /// Default's 0, below the premium tiers and the staff groups), parent Default, no nodes, not a premium tier.
    /// Idempotent: a group of that name (made by hand, or already in a dev database) is left alone, and both
    /// inserts are skipped then, so a stale LAST_INSERT_ID() is never used. Data only, no schema change.
    /// </summary>
    public partial class SeedAlphaTesterGroup : Migration
    {
        public const string Group = "Alpha Tester";

        public const int Weight = 1;

        public const string Parent = "Default";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TPT: a permission_holders row, then the permission_groups row with its Id, as in
            // SeedLocationRetentionStaffGroups. The parent is looked up by name; without a Default group it
            // stays NULL.
            migrationBuilder.Sql(
                "INSERT INTO permission_holders (ChatPrefix, ChatSuffix) " +
                $"SELECT NULL, NULL FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM permission_groups WHERE Name = '{Group}');");
            migrationBuilder.Sql(
                "INSERT INTO permission_groups (Id, Name, Weight, IsPremiumTier, SalaryMultiplier, ParentGroupId) " +
                $"SELECT LAST_INSERT_ID(), '{Group}', {Weight}, 0, 1.0, " +
                $"(SELECT d.Id FROM permission_groups d WHERE d.Name = '{Parent}' AND d.IsPremiumTier = 0 ORDER BY d.Id LIMIT 1) " +
                $"FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM permission_groups WHERE Name = '{Group}');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only a group nobody uses is removed: no members, no grants, and no invitation that grants it
            // (invitations.GrantGroupId is a Restrict foreign key).
            migrationBuilder.Sql(
                "DELETE ph FROM permission_holders ph INNER JOIN permission_groups pg ON pg.Id = ph.Id " +
                $"WHERE pg.Name = '{Group}' " +
                "AND NOT EXISTS (SELECT 1 FROM user_permission_groups m WHERE m.PermissionGroupId = pg.Id) " +
                "AND NOT EXISTS (SELECT 1 FROM permission_grants g WHERE g.HolderId = pg.Id) " +
                "AND NOT EXISTS (SELECT 1 FROM invitations i WHERE i.GrantGroupId = pg.Id);");
        }
    }
}
