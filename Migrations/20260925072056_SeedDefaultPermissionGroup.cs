using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class SeedDefaultPermissionGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Seeds the standard group every account should hold from creation (developer
            // request, 2026-09-25) - weight 0, not a premium tier. PermissionGroup is a TPT
            // subtype of PermissionHolder (docs/specs/user-features/DESIGN.md §1), so this needs
            // a row in both tables, same two-insert shape the Phase 1 migration itself used for
            // backfilling users -> permission_holders. permission_holders.Id is AUTO_INCREMENT
            // with no explicit value given here (unlike the title_brackets seed, which owns
            // fixed, well-known Ids by design) - LAST_INSERT_ID() is safe across the two
            // statements because EF Core runs a migration's Sql() calls sequentially on one
            // connection.
            migrationBuilder.Sql(
                "INSERT INTO permission_holders (ChatPrefix, ChatSuffix) VALUES (NULL, NULL); " +
                "INSERT INTO permission_groups (Id, Name, Weight, IsPremiumTier, SalaryMultiplier, ParentGroupId) " +
                "VALUES (LAST_INSERT_ID(), 'Default', 0, 0, 1.0, NULL);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE ph FROM permission_holders ph " +
                "INNER JOIN permission_groups pg ON pg.Id = ph.Id " +
                "WHERE pg.Name = 'Default' AND pg.ParentGroupId IS NULL;");
        }
    }
}
