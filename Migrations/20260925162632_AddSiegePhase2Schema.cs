using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddSiegePhase2Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "siege_configurations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CaptureAttackBase = table.Column<int>(type: "int", nullable: false),
                    CaptureAttackPerExtra = table.Column<int>(type: "int", nullable: false),
                    CaptureAttackPerExtraInstantVictory = table.Column<int>(type: "int", nullable: false),
                    CaptureDefendBase = table.Column<int>(type: "int", nullable: false),
                    CaptureDefendPerExtra = table.Column<int>(type: "int", nullable: false),
                    CaptureDefendPerExtraInstantVictory = table.Column<int>(type: "int", nullable: false),
                    SideCaptureReduction = table.Column<double>(type: "double", nullable: false),
                    VoteCloseSecondsBeforeStart = table.Column<int>(type: "int", nullable: false),
                    DrawSecondsBeforeStart = table.Column<int>(type: "int", nullable: false),
                    HubSecondsBeforeStart = table.Column<int>(type: "int", nullable: false),
                    TeamSplitSecondsBeforeStart = table.Column<int>(type: "int", nullable: false),
                    MatchmakingAnnouncementMarks = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    KillAnnouncementThresholds = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    KillStreakAnnounceAbove = table.Column<int>(type: "int", nullable: false),
                    HeadshotMultiplier = table.Column<double>(type: "double", nullable: false),
                    AllowedCommands = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SpawnPickerDelayTicks = table.Column<int>(type: "int", nullable: false),
                    EnchantDropChancePerMille = table.Column<int>(type: "int", nullable: false),
                    AllowedEnchantmentKeys = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EnchantLevelMin = table.Column<int>(type: "int", nullable: false),
                    EnchantLevelMax = table.Column<int>(type: "int", nullable: false),
                    MaxBooksAlive = table.Column<int>(type: "int", nullable: false),
                    NonMemberGateView = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_siege_configurations", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_lobbies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Mode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MatchmakingSeconds = table.Column<int>(type: "int", nullable: false),
                    CooldownSeconds = table.Column<int>(type: "int", nullable: false),
                    VoteCandidateCount = table.Column<int>(type: "int", nullable: false),
                    AllowRandomVote = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ScheduleJson = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_scenarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TownId = table.Column<int>(type: "int", nullable: false),
                    HubLocationId = table.Column<int>(type: "int", nullable: false),
                    PlayersMin = table.Column<int>(type: "int", nullable: false),
                    PlayersMax = table.Column<int>(type: "int", nullable: false),
                    MinTitleBracketId = table.Column<int>(type: "int", nullable: true),
                    MatchDurationMinSeconds = table.Column<int>(type: "int", nullable: false),
                    MatchDurationPerPlayerSeconds = table.Column<int>(type: "int", nullable: false),
                    MatchDurationMaxSeconds = table.Column<int>(type: "int", nullable: false),
                    CoinRewardWin = table.Column<int>(type: "int", nullable: false),
                    ExpRewardWin = table.Column<int>(type: "int", nullable: false),
                    GemRewardWin = table.Column<int>(type: "int", nullable: false),
                    CoinRewardHolding = table.Column<int>(type: "int", nullable: false),
                    ExpRewardHolding = table.Column<int>(type: "int", nullable: false),
                    CoinRewardCapture = table.Column<int>(type: "int", nullable: false),
                    ExpRewardCapture = table.Column<int>(type: "int", nullable: false),
                    LockdownScenarioArea = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AllowRecapture = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EnchantDropsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_scenarios_locations_HubLocationId",
                        column: x => x.HubLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_scenarios_title_brackets_MinTitleBracketId",
                        column: x => x.MinTitleBracketId,
                        principalTable: "title_brackets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_scenarios_towns_TownId",
                        column: x => x.TownId,
                        principalTable: "towns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_lobby_scenarios",
                columns: table => new
                {
                    SiegeLobbyId = table.Column<int>(type: "int", nullable: false),
                    SiegeScenarioId = table.Column<int>(type: "int", nullable: false),
                    Weight = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_siege_lobby_scenarios", x => new { x.SiegeLobbyId, x.SiegeScenarioId });
                    table.ForeignKey(
                        name: "FK_siege_lobby_scenarios_siege_lobbies_SiegeLobbyId",
                        column: x => x.SiegeLobbyId,
                        principalTable: "siege_lobbies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_siege_lobby_scenarios_siege_scenarios_SiegeScenarioId",
                        column: x => x.SiegeScenarioId,
                        principalTable: "siege_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_matches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeLobbyId = table.Column<int>(type: "int", nullable: false),
                    SiegeScenarioId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EndReason = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WinningAllianceGroup = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_matches_siege_lobbies_SiegeLobbyId",
                        column: x => x.SiegeLobbyId,
                        principalTable: "siege_lobbies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_matches_siege_scenarios_SiegeScenarioId",
                        column: x => x.SiegeScenarioId,
                        principalTable: "siege_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_scenario_districts",
                columns: table => new
                {
                    SiegeScenarioId = table.Column<int>(type: "int", nullable: false),
                    DistrictId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_siege_scenario_districts", x => new { x.SiegeScenarioId, x.DistrictId });
                    table.ForeignKey(
                        name: "FK_siege_scenario_districts_districts_DistrictId",
                        column: x => x.DistrictId,
                        principalTable: "districts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_scenario_districts_siege_scenarios_SiegeScenarioId",
                        column: x => x.SiegeScenarioId,
                        principalTable: "siege_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_teams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeScenarioId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AllianceGroup = table.Column<int>(type: "int", nullable: false),
                    ClanId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChatColor = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BannerDesignId = table.Column<int>(type: "int", nullable: true),
                    StartMessage = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_teams_banner_designs_BannerDesignId",
                        column: x => x.BannerDesignId,
                        principalTable: "banner_designs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_teams_clans_ClanId",
                        column: x => x.ClanId,
                        principalTable: "clans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_teams_siege_scenarios_SiegeScenarioId",
                        column: x => x.SiegeScenarioId,
                        principalTable: "siege_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_match_gate_snapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeMatchId = table.Column<int>(type: "int", nullable: false),
                    GateStructureId = table.Column<int>(type: "int", nullable: false),
                    SnapshotJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_match_gate_snapshots_gate_structures_GateStructureId",
                        column: x => x.GateStructureId,
                        principalTable: "gate_structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_match_gate_snapshots_siege_matches_SiegeMatchId",
                        column: x => x.SiegeMatchId,
                        principalTable: "siege_matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_match_participants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeMatchId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    SiegeTeamId = table.Column<int>(type: "int", nullable: true),
                    JoinedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LeftAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Kills = table.Column<int>(type: "int", nullable: false),
                    Deaths = table.Column<int>(type: "int", nullable: false),
                    HighestKillStreak = table.Column<int>(type: "int", nullable: false),
                    Captures = table.Column<int>(type: "int", nullable: false),
                    CoinsAwarded = table.Column<int>(type: "int", nullable: false),
                    ExpAwarded = table.Column<int>(type: "int", nullable: false),
                    GemsAwarded = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_match_participants_siege_matches_SiegeMatchId",
                        column: x => x.SiegeMatchId,
                        principalTable: "siege_matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_siege_match_participants_siege_teams_SiegeTeamId",
                        column: x => x.SiegeTeamId,
                        principalTable: "siege_teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_siege_match_participants_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_objectives",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeScenarioId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LocationId = table.Column<int>(type: "int", nullable: true),
                    GateStructureId = table.Column<int>(type: "int", nullable: true),
                    CapturePoints = table.Column<int>(type: "int", nullable: false),
                    CaptureRadius = table.Column<double>(type: "double", nullable: false),
                    InstantVictory = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    InitialHolderTeamId = table.Column<int>(type: "int", nullable: true),
                    SpawnWhenHeld = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    GateStateOnCapture = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_objectives_gate_structures_GateStructureId",
                        column: x => x.GateStructureId,
                        principalTable: "gate_structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_objectives_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_objectives_siege_scenarios_SiegeScenarioId",
                        column: x => x.SiegeScenarioId,
                        principalTable: "siege_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_siege_objectives_siege_teams_InitialHolderTeamId",
                        column: x => x.InitialHolderTeamId,
                        principalTable: "siege_teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_scenario_gates",
                columns: table => new
                {
                    SiegeScenarioId = table.Column<int>(type: "int", nullable: false),
                    GateStructureId = table.Column<int>(type: "int", nullable: false),
                    InitialOwnerTeamId = table.Column<int>(type: "int", nullable: true),
                    InitialState = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Damageable = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_siege_scenario_gates", x => new { x.SiegeScenarioId, x.GateStructureId });
                    table.ForeignKey(
                        name: "FK_siege_scenario_gates_gate_structures_GateStructureId",
                        column: x => x.GateStructureId,
                        principalTable: "gate_structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_scenario_gates_siege_scenarios_SiegeScenarioId",
                        column: x => x.SiegeScenarioId,
                        principalTable: "siege_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_siege_scenario_gates_siege_teams_InitialOwnerTeamId",
                        column: x => x.InitialOwnerTeamId,
                        principalTable: "siege_teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_spawnpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeTeamId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    SafeZoneRadius = table.Column<double>(type: "double", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_spawnpoints_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_spawnpoints_siege_teams_SiegeTeamId",
                        column: x => x.SiegeTeamId,
                        principalTable: "siege_teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_match_objective_results",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeMatchId = table.Column<int>(type: "int", nullable: false),
                    SiegeObjectiveId = table.Column<int>(type: "int", nullable: true),
                    FinalHolderTeamId = table.Column<int>(type: "int", nullable: true),
                    CapturedByUserId = table.Column<int>(type: "int", nullable: true),
                    CapturedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_match_objective_results_siege_matches_SiegeMatchId",
                        column: x => x.SiegeMatchId,
                        principalTable: "siege_matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_siege_match_objective_results_siege_objectives_SiegeObjectiv~",
                        column: x => x.SiegeObjectiveId,
                        principalTable: "siege_objectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_siege_match_objective_results_siege_teams_FinalHolderTeamId",
                        column: x => x.FinalHolderTeamId,
                        principalTable: "siege_teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_siege_match_objective_results_users_CapturedByUserId",
                        column: x => x.CapturedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_gate_structures_CurrentSiegeId",
                table: "gate_structures",
                column: "CurrentSiegeId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_lobbies_Key",
                table: "siege_lobbies",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_siege_lobby_scenarios_SiegeScenarioId",
                table: "siege_lobby_scenarios",
                column: "SiegeScenarioId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_match_gate_snapshots_GateStructureId",
                table: "siege_match_gate_snapshots",
                column: "GateStructureId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_match_gate_snapshots_SiegeMatchId_GateStructureId",
                table: "siege_match_gate_snapshots",
                columns: new[] { "SiegeMatchId", "GateStructureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_siege_match_objective_results_CapturedByUserId",
                table: "siege_match_objective_results",
                column: "CapturedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_match_objective_results_FinalHolderTeamId",
                table: "siege_match_objective_results",
                column: "FinalHolderTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_match_objective_results_SiegeMatchId",
                table: "siege_match_objective_results",
                column: "SiegeMatchId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_match_objective_results_SiegeObjectiveId",
                table: "siege_match_objective_results",
                column: "SiegeObjectiveId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_match_participants_SiegeMatchId_UserId",
                table: "siege_match_participants",
                columns: new[] { "SiegeMatchId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_siege_match_participants_SiegeTeamId",
                table: "siege_match_participants",
                column: "SiegeTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_match_participants_UserId",
                table: "siege_match_participants",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_matches_SiegeLobbyId",
                table: "siege_matches",
                column: "SiegeLobbyId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_matches_SiegeScenarioId",
                table: "siege_matches",
                column: "SiegeScenarioId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_matches_Status",
                table: "siege_matches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_siege_objectives_GateStructureId",
                table: "siege_objectives",
                column: "GateStructureId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_objectives_InitialHolderTeamId",
                table: "siege_objectives",
                column: "InitialHolderTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_objectives_LocationId",
                table: "siege_objectives",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_objectives_SiegeScenarioId_SortOrder",
                table: "siege_objectives",
                columns: new[] { "SiegeScenarioId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_siege_scenario_districts_DistrictId",
                table: "siege_scenario_districts",
                column: "DistrictId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_scenario_gates_GateStructureId",
                table: "siege_scenario_gates",
                column: "GateStructureId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_scenario_gates_InitialOwnerTeamId",
                table: "siege_scenario_gates",
                column: "InitialOwnerTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_scenarios_HubLocationId",
                table: "siege_scenarios",
                column: "HubLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_scenarios_MinTitleBracketId",
                table: "siege_scenarios",
                column: "MinTitleBracketId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_scenarios_TownId",
                table: "siege_scenarios",
                column: "TownId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_spawnpoints_LocationId",
                table: "siege_spawnpoints",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_spawnpoints_SiegeTeamId_SortOrder",
                table: "siege_spawnpoints",
                columns: new[] { "SiegeTeamId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_siege_teams_BannerDesignId",
                table: "siege_teams",
                column: "BannerDesignId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_teams_ClanId",
                table: "siege_teams",
                column: "ClanId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_teams_SiegeScenarioId_SortOrder",
                table: "siege_teams",
                columns: new[] { "SiegeScenarioId", "SortOrder" });

            // Hand-added: CurrentSiegeId was a plain int? placeholder ("FK to Siege (future)"), so any
            // value in it points at nothing - siege_matches was just created empty. Clear it, or the
            // new FK can't be added.
            migrationBuilder.Sql("UPDATE `gate_structures` SET `CurrentSiegeId` = NULL WHERE `CurrentSiegeId` IS NOT NULL;");

            migrationBuilder.AddForeignKey(
                name: "FK_gate_structures_siege_matches_CurrentSiegeId",
                table: "gate_structures",
                column: "CurrentSiegeId",
                principalTable: "siege_matches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_gate_structures_siege_matches_CurrentSiegeId",
                table: "gate_structures");

            migrationBuilder.DropTable(
                name: "siege_configurations");

            migrationBuilder.DropTable(
                name: "siege_lobby_scenarios");

            migrationBuilder.DropTable(
                name: "siege_match_gate_snapshots");

            migrationBuilder.DropTable(
                name: "siege_match_objective_results");

            migrationBuilder.DropTable(
                name: "siege_match_participants");

            migrationBuilder.DropTable(
                name: "siege_scenario_districts");

            migrationBuilder.DropTable(
                name: "siege_scenario_gates");

            migrationBuilder.DropTable(
                name: "siege_spawnpoints");

            migrationBuilder.DropTable(
                name: "siege_objectives");

            migrationBuilder.DropTable(
                name: "siege_matches");

            migrationBuilder.DropTable(
                name: "siege_teams");

            migrationBuilder.DropTable(
                name: "siege_lobbies");

            migrationBuilder.DropTable(
                name: "siege_scenarios");

            migrationBuilder.DropIndex(
                name: "IX_gate_structures_CurrentSiegeId",
                table: "gate_structures");
        }
    }
}
