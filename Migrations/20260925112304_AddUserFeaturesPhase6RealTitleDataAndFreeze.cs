using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// Replaces the Phase 4 placeholder title_brackets seed (which wrongly used v1's title-ID
    /// slot-unlock thresholds 0/5/10/12/15 as MinExperience instead of real XP amounts) with the
    /// real 19-bracket data from v1's live Titles table, adds Salary/CoinBonus/GemBonus/ExpBonus
    /// and gendered names, corrects the Phase 5 premium-tier SalaryMultiplier default, and adds
    /// User.Gender plus the admin-freeze fields (docs/specs/user-features rebuild of v1's
    /// FreezeCommands, a dead no-op stub in v1 — see ACTIVE_SESSIONS.md for the full round).
    /// </summary>
    public partial class AddUserFeaturesPhase6RealTitleDataAndFreeze : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Name",
                table: "title_brackets",
                newName: "MaleName");

            migrationBuilder.AddColumn<DateTime>(
                name: "FrozenAt",
                table: "users",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FrozenByUserId",
                table: "users",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FrozenReason",
                table: "users",
                type: "longtext",
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "Gender",
                table: "users",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFrozen",
                table: "users",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CoinBonus",
                table: "title_brackets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ExpBonus",
                table: "title_brackets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FemaleName",
                table: "title_brackets",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "GemBonus",
                table: "title_brackets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Salary",
                table: "title_brackets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Replace the placeholder seed (5 brackets, MinExperience wrongly seeded as v1's
            // title-ID slot-unlock thresholds 0/5/10/12/15 instead of real XP amounts — see this
            // migration's class doc comment) with the real 19-bracket "Titles" table the
            // developer supplied from a v1 phpMyAdmin export (KnightsAndKings (1).json).
            migrationBuilder.Sql("DELETE FROM title_brackets;");
            migrationBuilder.Sql(@"
INSERT INTO title_brackets (Id, MaleName, FemaleName, Salary, CoinBonus, GemBonus, ExpBonus, MinExperience) VALUES
(0, 'Serf', 'Serf', 650, 0, 0, 0, 0),
(1, 'Peasant', 'Peasant', 1350, 13500, 3, 32, 2500),
(2, 'Yeoman', 'Yeoman', 1650, 16500, 3, 40, 3200),
(3, 'Squire', 'Squire', 2000, 20000, 4, 45, 4000),
(4, 'Reeve', 'Reeve', 2400, 24000, 4, 100, 4500),
(5, 'Knight', 'Dame', 4800, 48000, 50, 115, 10000),
(6, 'Baronet', 'Baronetess', 5750, 57500, 5, 130, 11500),
(7, 'Baron', 'Baroness', 7000, 70000, 5, 165, 13000),
(8, 'Viscount', 'Viscountess', 8250, 82500, 5, 200, 16500),
(9, 'Count', 'Countess', 10000, 100000, 5, 250, 20000),
(10, 'Margrave', 'Margravine', 15000, 150000, 100, 300, 30000),
(11, 'Duke', 'Duchess', 18000, 180000, 6, 350, 35500),
(12, 'Grand Duke', 'Grand Duchess', 21500, 215000, 50, 375, 42500),
(13, 'Regent', 'Regent', 25750, 257500, 10, 400, 50000),
(14, 'Prince Consort', 'Princess Consort', 30850, 308500, 10, 500, 60000),
(15, 'Prince', 'Princess', 46300, 463000, 50, 600, 92500),
(16, 'King', 'Queen', 55555, 555550, 20, 750, 110000),
(17, 'Emperor', 'Empress', 66666, 666660, 20, 850, 130000),
(18, 'One of the Seven', 'One of the Seven', 80000, 800000, 250, 1200, 160000);");

            // v1's Donator table gives the real Noble/Royal/Dragon Blood salary multipliers
            // (1.10/1.20/1.50) — the Phase 5 premium-tier seed migration left SalaryMultiplier at
            // its 1.0 default for all three since the real values weren't known at the time.
            migrationBuilder.Sql(@"
UPDATE permission_groups SET SalaryMultiplier = 1.10 WHERE Name = 'Noble';
UPDATE permission_groups SET SalaryMultiplier = 1.20 WHERE Name = 'Royal';
UPDATE permission_groups SET SalaryMultiplier = 1.50 WHERE Name = 'Dragon Blood';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE permission_groups SET SalaryMultiplier = 1.0 WHERE Name IN ('Noble', 'Royal', 'Dragon Blood');");
            migrationBuilder.Sql("DELETE FROM title_brackets;");

            migrationBuilder.DropColumn(
                name: "FrozenAt",
                table: "users");

            migrationBuilder.DropColumn(
                name: "FrozenByUserId",
                table: "users");

            migrationBuilder.DropColumn(
                name: "FrozenReason",
                table: "users");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "users");

            migrationBuilder.DropColumn(
                name: "IsFrozen",
                table: "users");

            migrationBuilder.DropColumn(
                name: "CoinBonus",
                table: "title_brackets");

            migrationBuilder.DropColumn(
                name: "ExpBonus",
                table: "title_brackets");

            migrationBuilder.DropColumn(
                name: "FemaleName",
                table: "title_brackets");

            migrationBuilder.DropColumn(
                name: "GemBonus",
                table: "title_brackets");

            migrationBuilder.DropColumn(
                name: "Salary",
                table: "title_brackets");

            migrationBuilder.RenameColumn(
                name: "MaleName",
                table: "title_brackets",
                newName: "Name");
        }
    }
}
