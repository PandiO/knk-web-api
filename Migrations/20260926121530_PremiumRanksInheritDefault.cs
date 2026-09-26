using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class PremiumRanksInheritDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A player holds exactly one rank - the free Default or one of the paid premium tiers,
            // which upgrade from it - so the Player manager removes Default when it switches them to
            // Noble/Royal/Dragon Blood (knk-plugin UserAdminService.setRank). Making the tiers
            // inherit from Default keeps whatever Default grants for the players who upgraded
            // instead of dropping it with the membership. No effect today: Default has no seeded
            // grants, and accounts created since SeedDefaultPermissionGroup hold Default anyway.
            // Only tiers without a parent are
            // touched, so an admin-chosen parent is kept; MySQL can't UPDATE a table from a
            // subquery on itself, hence the self-join.
            migrationBuilder.Sql(@"
UPDATE permission_groups g
JOIN permission_groups d ON d.Name = 'Default' AND d.IsPremiumTier = 0
SET g.ParentGroupId = d.Id
WHERE g.Name IN ('Noble', 'Royal', 'Dragon Blood') AND g.IsPremiumTier = 1 AND g.ParentGroupId IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE permission_groups g
JOIN permission_groups d ON d.Name = 'Default' AND g.ParentGroupId = d.Id
SET g.ParentGroupId = NULL
WHERE g.Name IN ('Noble', 'Royal', 'Dragon Blood') AND g.IsPremiumTier = 1;");
        }
    }
}
