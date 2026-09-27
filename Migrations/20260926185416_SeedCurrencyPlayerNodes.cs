using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// Grants the player currency nodes (currency-payments DESIGN.md §3.8, IMPLEMENTATION_PLAN.md
    /// Phase 3) to the "Default" group every account holds: /pay, /balance (own and others'),
    /// /baltop and /transactions. Data only - no schema change. A node the group already holds
    /// (granted or denied by an admin) is left as it is; with no Default group (see
    /// SeedDefaultPermissionGroup) nothing is inserted.
    /// </summary>
    public partial class SeedCurrencyPlayerNodes : Migration
    {
        private static readonly string[] Nodes =
        {
            "knk.pay", "knk.balance", "knk.balance.others", "knk.baltop", "knk.transactions"
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var node in Nodes)
            {
                migrationBuilder.Sql(
                    "INSERT INTO permission_grants (HolderId, Node, Value, ExpiresAt) " +
                    $"SELECT pg.Id, '{node}', 1, NULL FROM permission_groups pg " +
                    "WHERE pg.Name = 'Default' AND pg.ParentGroupId IS NULL " +
                    $"AND NOT EXISTS (SELECT 1 FROM permission_grants g WHERE g.HolderId = pg.Id AND g.Node = '{node}');");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Removes the plain grants Up adds (an admin's deny or expiring grant is kept).
            foreach (var node in Nodes)
            {
                migrationBuilder.Sql(
                    "DELETE g FROM permission_grants g " +
                    "INNER JOIN permission_groups pg ON pg.Id = g.HolderId " +
                    $"WHERE pg.Name = 'Default' AND pg.ParentGroupId IS NULL AND g.Node = '{node}' AND g.Value = 1 AND g.ExpiresAt IS NULL;");
            }
        }
    }
}
