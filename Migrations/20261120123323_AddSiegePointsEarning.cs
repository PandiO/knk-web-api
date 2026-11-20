using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Siege shop L1 (PandiO/knk-workspace#866): siege points earn settings and the PvP transfer switch on
    /// SiegeConfiguration (the existing row gets the DESIGN § 5.2 defaults, transfers off), and the siege points earned
    /// and spent per SiegeMatchParticipant (0 for past matches). Additive only.
    /// </summary>
    public partial class AddSiegePointsEarning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SiegePointsEarned",
                table: "siege_match_participants",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SiegePointsSpent",
                table: "siege_match_participants",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "AssistMinDamage",
                table: "siege_configurations",
                type: "double",
                nullable: false,
                defaultValue: 4.0);   // siege shop § 5.2 default (#866)

            migrationBuilder.AddColumn<int>(
                name: "AssistWindowSeconds",
                table: "siege_configurations",
                type: "int",
                nullable: false,
                defaultValue: 10);   // siege shop § 5.2 default (#866)

            migrationBuilder.AddColumn<double>(
                name: "GateHpPerPoint",
                table: "siege_configurations",
                type: "double",
                nullable: false,
                defaultValue: 5.0);   // siege shop § 5.2 default (#866)

            migrationBuilder.AddColumn<bool>(
                name: "PointTransfersEnabled",
                table: "siege_configurations",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PointsPerAssist",
                table: "siege_configurations",
                type: "int",
                nullable: false,
                defaultValue: 40);   // siege shop § 5.2 default (#866)

            migrationBuilder.AddColumn<int>(
                name: "PointsPerCapture",
                table: "siege_configurations",
                type: "int",
                nullable: false,
                defaultValue: 100);   // siege shop § 5.2 default (#866)

            migrationBuilder.AddColumn<int>(
                name: "PointsPerCapturePercent",
                table: "siege_configurations",
                type: "int",
                nullable: false,
                defaultValue: 3);   // siege shop § 5.2 default (#866)

            migrationBuilder.AddColumn<int>(
                name: "PointsPerDeath",
                table: "siege_configurations",
                type: "int",
                nullable: false,
                defaultValue: 15);   // siege shop § 5.2 default (#866)

            migrationBuilder.AddColumn<int>(
                name: "PointsPerKill",
                table: "siege_configurations",
                type: "int",
                nullable: false,
                defaultValue: 100);   // siege shop § 5.2 default (#866)

            migrationBuilder.AddColumn<double>(
                name: "PointsStreakCap",
                table: "siege_configurations",
                type: "double",
                nullable: false,
                defaultValue: 1.5);   // siege shop § 5.2 default (#866)

            migrationBuilder.AddColumn<double>(
                name: "PointsStreakStep",
                table: "siege_configurations",
                type: "double",
                nullable: false,
                defaultValue: 0.1);   // siege shop § 5.2 default (#866)

            migrationBuilder.AddColumn<int>(
                name: "TrickleAmount",
                table: "siege_configurations",
                type: "int",
                nullable: false,
                defaultValue: 10);   // siege shop § 5.2 default (#866)

            migrationBuilder.AddColumn<int>(
                name: "TrickleIntervalSeconds",
                table: "siege_configurations",
                type: "int",
                nullable: false,
                defaultValue: 60);   // siege shop § 5.2 default (#866)
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SiegePointsEarned",
                table: "siege_match_participants");

            migrationBuilder.DropColumn(
                name: "SiegePointsSpent",
                table: "siege_match_participants");

            migrationBuilder.DropColumn(
                name: "AssistMinDamage",
                table: "siege_configurations");

            migrationBuilder.DropColumn(
                name: "AssistWindowSeconds",
                table: "siege_configurations");

            migrationBuilder.DropColumn(
                name: "GateHpPerPoint",
                table: "siege_configurations");

            migrationBuilder.DropColumn(
                name: "PointTransfersEnabled",
                table: "siege_configurations");

            migrationBuilder.DropColumn(
                name: "PointsPerAssist",
                table: "siege_configurations");

            migrationBuilder.DropColumn(
                name: "PointsPerCapture",
                table: "siege_configurations");

            migrationBuilder.DropColumn(
                name: "PointsPerCapturePercent",
                table: "siege_configurations");

            migrationBuilder.DropColumn(
                name: "PointsPerDeath",
                table: "siege_configurations");

            migrationBuilder.DropColumn(
                name: "PointsPerKill",
                table: "siege_configurations");

            migrationBuilder.DropColumn(
                name: "PointsStreakCap",
                table: "siege_configurations");

            migrationBuilder.DropColumn(
                name: "PointsStreakStep",
                table: "siege_configurations");

            migrationBuilder.DropColumn(
                name: "TrickleAmount",
                table: "siege_configurations");

            migrationBuilder.DropColumn(
                name: "TrickleIntervalSeconds",
                table: "siege_configurations");
        }
    }
}
