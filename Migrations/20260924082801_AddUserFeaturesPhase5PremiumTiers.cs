using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddUserFeaturesPhase5PremiumTiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPremiumTier",
                table: "permission_groups",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            // Seed v1's three donator tiers as premium PermissionGroups (developer-confirmed
            // names, IMPLEMENTATION_PLAN.md §5; v1 ids noble=1/royal=2/dragon blood=3 per
            // docs/specs/legacy/user-system.md). No grants — tier perks are authored later.
            //
            // Can't use InsertData with fixed ids: PermissionGroup is a TPT subtype, so its id is
            // drawn from permission_holders' AUTO_INCREMENT, shared with every User. Insert the
            // holder row first and reuse LAST_INSERT_ID() (per-connection, so safe here). Each
            // tier is skipped if a group with that name already exists (Name is unique), leaving
            // an admin-created group of the same name untouched.
            foreach (var (name, weight) in SeededTiers)
            {
                migrationBuilder.Sql($@"
INSERT INTO permission_holders (ChatPrefix, ChatSuffix)
SELECT NULL, NULL FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM permission_groups WHERE Name = '{name}');");

                migrationBuilder.Sql($@"
INSERT INTO permission_groups (Id, Name, Weight, ParentGroupId, IsPremiumTier)
SELECT LAST_INSERT_ID(), '{name}', {weight}, NULL, 1 FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM permission_groups WHERE Name = '{name}');");
            }
        }

        private static readonly (string Name, int Weight)[] SeededTiers =
        {
            ("Noble", 10),
            ("Royal", 20),
            ("Dragon Blood", 30)
        };

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove the seeded tiers (only while still flagged premium). Deleting the
            // permission_holders row cascades to the permission_groups row, its memberships and
            // its grants. Fails (RESTRICT) if someone has since made a tier another group's parent.
            migrationBuilder.Sql(@"
DELETE h FROM permission_holders h
JOIN permission_groups g ON g.Id = h.Id
WHERE g.IsPremiumTier = 1 AND g.Name IN ('Noble', 'Royal', 'Dragon Blood');");

            migrationBuilder.DropColumn(
                name: "IsPremiumTier",
                table: "permission_groups");
        }
    }
}
