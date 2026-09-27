using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class BackfillDefaultRankMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SeedDefaultPermissionGroup (2026-09-25) created the Default group but only new accounts
            // were put in it (UserService.AssignDefaultGroupAsync), so older accounts inherited none of
            // Default's grants (lootbox smoke test 2026-09-27: knk.lootbox.odds refused for a non-op
            // until it was granted personally). Every account without an active rank - Default or a
            // premium tier - gets Default, permanently: an expired Default row is made permanent again,
            // otherwise a row is added. Same rule as UserPermissionGroupService.EnsureDefaultRankAsync.
            // MySQL can't UPDATE a table from a subquery on itself, hence the materialized (DISTINCT) derived table.
            migrationBuilder.Sql(@"
UPDATE user_permission_groups m
JOIN permission_groups d ON d.Id = m.PermissionGroupId AND d.Name = 'Default' AND d.IsPremiumTier = 0
JOIN (
    SELECT DISTINCT u.Id AS UserId FROM users u
    WHERE NOT EXISTS (
        SELECT 1 FROM user_permission_groups r
        JOIN permission_groups g ON g.Id = r.PermissionGroupId
        WHERE r.UserId = u.Id
          AND (r.ExpiresAt IS NULL OR r.ExpiresAt > UTC_TIMESTAMP())
          AND (g.IsPremiumTier = 1 OR (g.Name = 'Default' AND g.IsPremiumTier = 0)))
) rankless ON rankless.UserId = m.UserId
SET m.ExpiresAt = NULL;");

            migrationBuilder.Sql(@"
INSERT INTO user_permission_groups (UserId, PermissionGroupId, ExpiresAt)
SELECT u.Id, d.Id, NULL
FROM users u
JOIN permission_groups d ON d.Name = 'Default' AND d.IsPremiumTier = 0 AND d.ParentGroupId IS NULL
WHERE NOT EXISTS (SELECT 1 FROM user_permission_groups x WHERE x.UserId = u.Id AND x.PermissionGroupId = d.Id)
  AND NOT EXISTS (
      SELECT 1 FROM user_permission_groups r
      JOIN permission_groups g ON g.Id = r.PermissionGroupId
      WHERE r.UserId = u.Id AND g.IsPremiumTier = 1
        AND (r.ExpiresAt IS NULL OR r.ExpiresAt > UTC_TIMESTAMP()));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: a backfilled row can't be told apart from one the rank logic added.
        }
    }
}
