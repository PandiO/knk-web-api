using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// Closed-alpha test run 1, package E (PandiO/knk-workspace#165 gate damage reward, #166 participation reward):
    /// the scenario's gate damage reward (coins/XP per full <c>GateDamageRewardStep</c> HP) and participation reward
    /// (coins/XP for a member active at least <c>ParticipationMinActivePercent</c> of their match time), and the
    /// participant's reported gate damage, active seconds and match seconds. Existing scenarios get the model
    /// defaults, so they pay both rewards from the next match on; existing participant rows get 0.
    /// </summary>
    public partial class AddSiegeGateDamageAndParticipationRewards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GateDamageRewardStep",
                table: "siege_scenarios",
                type: "int",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<int>(
                name: "CoinRewardGateDamage",
                table: "siege_scenarios",
                type: "int",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "ExpRewardGateDamage",
                table: "siege_scenarios",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CoinRewardParticipation",
                table: "siege_scenarios",
                type: "int",
                nullable: false,
                defaultValue: 25);

            migrationBuilder.AddColumn<int>(
                name: "ExpRewardParticipation",
                table: "siege_scenarios",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "ParticipationMinActivePercent",
                table: "siege_scenarios",
                type: "int",
                nullable: false,
                defaultValue: 75);

            migrationBuilder.AddColumn<int>(
                name: "GateDamage",
                table: "siege_match_participants",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ActiveSeconds",
                table: "siege_match_participants",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MatchSeconds",
                table: "siege_match_participants",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GateDamageRewardStep",
                table: "siege_scenarios");

            migrationBuilder.DropColumn(
                name: "CoinRewardGateDamage",
                table: "siege_scenarios");

            migrationBuilder.DropColumn(
                name: "ExpRewardGateDamage",
                table: "siege_scenarios");

            migrationBuilder.DropColumn(
                name: "CoinRewardParticipation",
                table: "siege_scenarios");

            migrationBuilder.DropColumn(
                name: "ExpRewardParticipation",
                table: "siege_scenarios");

            migrationBuilder.DropColumn(
                name: "ParticipationMinActivePercent",
                table: "siege_scenarios");

            migrationBuilder.DropColumn(
                name: "GateDamage",
                table: "siege_match_participants");

            migrationBuilder.DropColumn(
                name: "ActiveSeconds",
                table: "siege_match_participants");

            migrationBuilder.DropColumn(
                name: "MatchSeconds",
                table: "siege_match_participants");
        }
    }
}
