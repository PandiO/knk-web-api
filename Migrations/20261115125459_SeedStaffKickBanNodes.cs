using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// Staff may kick and ban from the web app (PandiO/knk-workspace#565, developer decision 2026-11-15): gives the
    /// <b>Staff</b> group <c>knk.kick</c> and <c>knk.ban</c>, the nodes <c>POST api/Users/{id}/kick</c> and
    /// <c>/ban</c> check. The ranks above Staff get them by inheriting from Staff (alpha: Staff → Moderator → Admin) or
    /// through <c>*</c>. Data only, no schema change; modelled on <see cref="SeedStaffFreezeNodes"/>.
    ///
    /// <para>Idempotent: the group is found by name, and a node is skipped when the group is missing (a database made by
    /// migrations only has no Staff group) or when Staff or a group it inherits from already has a grant or deny of the
    /// node or a wildcard covering it (<see cref="SeedDashboardActionNodes.Decides"/>). A deny is never overridden.</para>
    ///
    /// <para>Down removes nothing, for the reason <see cref="SeedStaffFreezeNodes"/> gives: a grant this Up added can't
    /// be told apart from the same grant given by hand. A grant left behind by a rollback is removed on the group's
    /// page in the web app.</para>
    /// </summary>
    public partial class SeedStaffKickBanNodes : Migration
    {
        public const string Kick = "knk.kick";
        public const string Ban = "knk.ban";

        /// <summary>The nodes Staff gets.</summary>
        public static readonly string[] Nodes = { Kick, Ban };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var node in Nodes)
            {
                migrationBuilder.Sql(SeedStaffFreezeNodes.GrantSql(SeedStaffFreezeNodes.StaffGroup, node));
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty, see the class summary.
        }
    }
}
