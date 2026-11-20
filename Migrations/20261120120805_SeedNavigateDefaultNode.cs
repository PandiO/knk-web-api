using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// <c>/navigate</c> can be taken away (PandiO/knk-workspace#845, developer decision option A, 2026-11-19): the
    /// plugin's <c>plugin.yml</c> declares <c>knk.navigate</c> as <c>default: false</c> from now on, so Bukkit no longer
    /// answers yes for every player and an in-house deny decides. Every player keeps <c>/navigate</c> through this seed,
    /// which gives the <b>Default</b> group (the group every account holds, see <see cref="SeedDefaultPermissionGroup"/>)
    /// the node. Data only, no schema change; modelled on <see cref="SeedStaffFreezeNodes"/>.
    ///
    /// <para>Deploy order: apply this migration before the plugin with <c>default: false</c>, or every player loses
    /// <c>/navigate</c> until it is applied.</para>
    ///
    /// <para>Idempotent: the group is found by name, and the node is skipped when the group is missing or when Default
    /// or a group it inherits from already has a grant or deny of the node or a wildcard covering it
    /// (<see cref="SeedDashboardActionNodes.Decides"/>). A deny is never overridden. <c>knk.navigate.any</c> is not
    /// seeded here: the staff rank(s) that get it are still open (#845).</para>
    ///
    /// <para>Down removes nothing, for the reason <see cref="SeedStaffFreezeNodes"/> gives: a grant this Up added can't
    /// be told apart from the same grant given by hand, and taking <c>/navigate</c> away from every player on a rollback
    /// (while the plugin may already be the new one) would be the worse mistake. A grant left behind by a rollback is
    /// harmless with the older plugin (<c>default: true</c>) and is removed on the group's page in the web app.</para>
    /// </summary>
    public partial class SeedNavigateDefaultNode : Migration
    {
        public const string Navigate = "knk.navigate";

        /// <summary>The group every account holds.</summary>
        public const string DefaultGroup = "Default";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SeedStaffFreezeNodes.GrantSql(DefaultGroup, Navigate));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty, see the class summary.
        }
    }
}
