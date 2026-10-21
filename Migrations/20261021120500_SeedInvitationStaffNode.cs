using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using knkwebapi_v2.Properties;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// KNG-212 (docs/specs/invitations/IMPLEMENTATION_PLAN.md § 5.1): grants knk.admin.invites to the
    /// Admin group, the same way SeedStatisticsStaffNodes grants its nodes. A missing group or an
    /// existing grant is left alone. Data only, no schema change.
    /// </summary>
    [DbContext(typeof(KnKDbContext))]
    [Migration("20261021120500_SeedInvitationStaffNode")]
    public partial class SeedInvitationStaffNode : Migration
    {
        public const string Group = "Admin";

        public const string Node = "knk.admin.invites";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "INSERT INTO permission_grants (HolderId, Node, Value, ExpiresAt) " +
                $"SELECT pg.Id, '{Node}', 1, NULL FROM permission_groups pg WHERE pg.Name = '{Group}' " +
                $"AND NOT EXISTS (SELECT 1 FROM permission_grants g WHERE g.HolderId = pg.Id AND g.Node = '{Node}');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE g FROM permission_grants g INNER JOIN permission_groups pg ON pg.Id = g.HolderId " +
                $"WHERE pg.Name = '{Group}' AND g.Node = '{Node}' AND g.Value = 1 AND g.ExpiresAt IS NULL;");
        }
    }
}
