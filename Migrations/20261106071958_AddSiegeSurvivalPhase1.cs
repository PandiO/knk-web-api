using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddSiegeSurvivalPhase1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "siege_survival_difficulty_profiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeScenarioId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CountPreset = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CountBase = table.Column<double>(type: "double", nullable: false),
                    CountStep = table.Column<double>(type: "double", nullable: false),
                    CountMultiplier = table.Column<double>(type: "double", nullable: false),
                    CountSwitchRound = table.Column<int>(type: "int", nullable: false),
                    StatPreset = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StatStep = table.Column<double>(type: "double", nullable: false),
                    StatMultiplier = table.Column<double>(type: "double", nullable: false),
                    StatSwitchRound = table.Column<int>(type: "int", nullable: false),
                    HealthWeight = table.Column<double>(type: "double", nullable: false),
                    WalkingSpeedWeight = table.Column<double>(type: "double", nullable: false),
                    MeleeDamageWeight = table.Column<double>(type: "double", nullable: false),
                    RangedDamageWeight = table.Column<double>(type: "double", nullable: false),
                    AttackSpeedWeight = table.Column<double>(type: "double", nullable: false),
                    PerPlayerFactor = table.Column<double>(type: "double", nullable: false),
                    PerPlayerAppliesToStats = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MaxAlive = table.Column<int>(type: "int", nullable: false),
                    StatCap = table.Column<double>(type: "double", nullable: false),
                    CountCap = table.Column<int>(type: "int", nullable: false),
                    RepairRatePerSecond = table.Column<double>(type: "double", nullable: false),
                    GateDamageFactor = table.Column<double>(type: "double", nullable: false),
                    CaptureContributionWeight = table.Column<double>(type: "double", nullable: false),
                    SpawnIntervalMillis = table.Column<int>(type: "int", nullable: false),
                    AggroRadius = table.Column<double>(type: "double", nullable: true),
                    LeashDistance = table.Column<double>(type: "double", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_survival_difficulty_profiles_siege_scenarios_SiegeScen~",
                        column: x => x.SiegeScenarioId,
                        principalTable: "siege_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_survival_lobby_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeLobbyId = table.Column<int>(type: "int", nullable: false),
                    AllCapturedOverride = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_survival_lobby_settings_siege_lobbies_SiegeLobbyId",
                        column: x => x.SiegeLobbyId,
                        principalTable: "siege_lobbies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_survival_monster_types",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeScenarioId = table.Column<int>(type: "int", nullable: false),
                    EntityType = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Weight = table.Column<double>(type: "double", nullable: false),
                    UnlockRound = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_survival_monster_types_siege_scenarios_SiegeScenarioId",
                        column: x => x.SiegeScenarioId,
                        principalTable: "siege_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_survival_mystery_box_pools",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeScenarioId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Variant = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_survival_mystery_box_pools_siege_scenarios_SiegeScenar~",
                        column: x => x.SiegeScenarioId,
                        principalTable: "siege_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_survival_purchase_points",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeScenarioId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    InteractionRange = table.Column<double>(type: "double", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_survival_purchase_points_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_survival_purchase_points_siege_scenarios_SiegeScenario~",
                        column: x => x.SiegeScenarioId,
                        principalTable: "siege_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_survival_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeScenarioId = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MinPlayers = table.Column<int>(type: "int", nullable: false),
                    HumanTeamId = table.Column<int>(type: "int", nullable: true),
                    NpcTeamId = table.Column<int>(type: "int", nullable: true),
                    EquipmentMode = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DefaultKitId = table.Column<int>(type: "int", nullable: true),
                    BaselineLoadoutKitId = table.Column<int>(type: "int", nullable: true),
                    IntermissionMillis = table.Column<int>(type: "int", nullable: false),
                    EndWhenAllObjectivesCaptured = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    FavorNearPlayersPercent = table.Column<int>(type: "int", nullable: false),
                    NearZoneCount = table.Column<int>(type: "int", nullable: false),
                    StuckAfterMillis = table.Column<int>(type: "int", nullable: false),
                    LastEnemiesCount = table.Column<int>(type: "int", nullable: false),
                    RespawnLimit = table.Column<int>(type: "int", nullable: false),
                    RoundBackstopMillis = table.Column<int>(type: "int", nullable: true),
                    MeaningfulDistance = table.Column<double>(type: "double", nullable: false),
                    CoinsPerKill = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    XpPerKill = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    MatchCurrencyPerKill = table.Column<int>(type: "int", nullable: false),
                    KillBonusMultiplier = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_survival_settings_kits_BaselineLoadoutKitId",
                        column: x => x.BaselineLoadoutKitId,
                        principalTable: "kits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_survival_settings_kits_DefaultKitId",
                        column: x => x.DefaultKitId,
                        principalTable: "kits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_survival_settings_siege_scenarios_SiegeScenarioId",
                        column: x => x.SiegeScenarioId,
                        principalTable: "siege_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_siege_survival_settings_siege_teams_HumanTeamId",
                        column: x => x.HumanTeamId,
                        principalTable: "siege_teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_siege_survival_settings_siege_teams_NpcTeamId",
                        column: x => x.NpcTeamId,
                        principalTable: "siege_teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_survival_spawn_zones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeScenarioId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Weight = table.Column<double>(type: "double", nullable: false),
                    AnchorLocationId = table.Column<int>(type: "int", nullable: false),
                    AreaRadius = table.Column<double>(type: "double", nullable: true),
                    PrerequisitePolicy = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MinWave = table.Column<int>(type: "int", nullable: false),
                    MaxWave = table.Column<int>(type: "int", nullable: true),
                    ForNormalWaves = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ForSpecialWaves = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LaneAutoReroute = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_survival_spawn_zones_locations_AnchorLocationId",
                        column: x => x.AnchorLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_survival_spawn_zones_siege_scenarios_SiegeScenarioId",
                        column: x => x.SiegeScenarioId,
                        principalTable: "siege_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_survival_special_wave_profiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeScenarioId = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CountOverride = table.Column<int>(type: "int", nullable: true),
                    CountMultiplier = table.Column<double>(type: "double", nullable: false),
                    Additive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_survival_special_wave_profiles_siege_scenarios_SiegeSc~",
                        column: x => x.SiegeScenarioId,
                        principalTable: "siege_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_survival_target_overrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeScenarioId = table.Column<int>(type: "int", nullable: false),
                    SiegeObjectiveId = table.Column<int>(type: "int", nullable: true),
                    GateStructureId = table.Column<int>(type: "int", nullable: true),
                    Mode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_survival_target_overrides_gate_structures_GateStructur~",
                        column: x => x.GateStructureId,
                        principalTable: "gate_structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_siege_survival_target_overrides_siege_objectives_SiegeObject~",
                        column: x => x.SiegeObjectiveId,
                        principalTable: "siege_objectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_siege_survival_target_overrides_siege_scenarios_SiegeScenari~",
                        column: x => x.SiegeScenarioId,
                        principalTable: "siege_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_survival_mystery_box_entries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SurvivalMysteryBoxPoolId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: true),
                    EnchantmentDefinitionId = table.Column<int>(type: "int", nullable: true),
                    EnchantmentLevel = table.Column<int>(type: "int", nullable: true),
                    ChancePercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_survival_mystery_box_entries_EnchantmentDefinitions_En~",
                        column: x => x.EnchantmentDefinitionId,
                        principalTable: "EnchantmentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_survival_mystery_box_entries_item_blueprints_ItemBluep~",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_survival_mystery_box_entries_siege_survival_mystery_bo~",
                        column: x => x.SurvivalMysteryBoxPoolId,
                        principalTable: "siege_survival_mystery_box_pools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_survival_mystery_box_points",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeScenarioId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    InteractionRange = table.Column<double>(type: "double", nullable: false),
                    SurvivalMysteryBoxPoolId = table.Column<int>(type: "int", nullable: false),
                    BlockType = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RollPrice = table.Column<int>(type: "int", nullable: false),
                    ClaimTimeoutMillis = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_survival_mystery_box_points_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_survival_mystery_box_points_siege_scenarios_SiegeScena~",
                        column: x => x.SiegeScenarioId,
                        principalTable: "siege_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_siege_survival_mystery_box_points_siege_survival_mystery_box~",
                        column: x => x.SurvivalMysteryBoxPoolId,
                        principalTable: "siege_survival_mystery_box_pools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_survival_purchase_offers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SurvivalPurchasePointId = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_survival_purchase_offers_item_blueprints_ItemBlueprint~",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_survival_purchase_offers_siege_survival_purchase_point~",
                        column: x => x.SurvivalPurchasePointId,
                        principalTable: "siege_survival_purchase_points",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_survival_settings_kits",
                columns: table => new
                {
                    SiegeSurvivalSettingsId = table.Column<int>(type: "int", nullable: false),
                    KitId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_siege_survival_settings_kits", x => new { x.SiegeSurvivalSettingsId, x.KitId });
                    table.ForeignKey(
                        name: "FK_siege_survival_settings_kits_kits_KitId",
                        column: x => x.KitId,
                        principalTable: "kits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_survival_settings_kits_siege_survival_settings_SiegeSu~",
                        column: x => x.SiegeSurvivalSettingsId,
                        principalTable: "siege_survival_settings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_survival_spawn_zone_objectives",
                columns: table => new
                {
                    SurvivalSpawnZoneId = table.Column<int>(type: "int", nullable: false),
                    SiegeObjectiveId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_siege_survival_spawn_zone_objectives", x => new { x.SurvivalSpawnZoneId, x.SiegeObjectiveId });
                    table.ForeignKey(
                        name: "FK_siege_survival_spawn_zone_objectives_siege_objectives_SiegeO~",
                        column: x => x.SiegeObjectiveId,
                        principalTable: "siege_objectives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_siege_survival_spawn_zone_objectives_siege_survival_spawn_zo~",
                        column: x => x.SurvivalSpawnZoneId,
                        principalTable: "siege_survival_spawn_zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_survival_spawn_zone_points",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SurvivalSpawnZoneId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LocationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_survival_spawn_zone_points_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_survival_spawn_zone_points_siege_survival_spawn_zones_~",
                        column: x => x.SurvivalSpawnZoneId,
                        principalTable: "siege_survival_spawn_zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_survival_special_wave_entries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeScenarioId = table.Column<int>(type: "int", nullable: false),
                    SurvivalSpecialWaveProfileId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Interval = table.Column<int>(type: "int", nullable: true),
                    StartWave = table.Column<int>(type: "int", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Wave = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_survival_special_wave_entries_siege_scenarios_SiegeSce~",
                        column: x => x.SiegeScenarioId,
                        principalTable: "siege_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_siege_survival_special_wave_entries_siege_survival_special_w~",
                        column: x => x.SurvivalSpecialWaveProfileId,
                        principalTable: "siege_survival_special_wave_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_survival_special_wave_monster_types",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SurvivalSpecialWaveProfileId = table.Column<int>(type: "int", nullable: false),
                    EntityType = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Weight = table.Column<double>(type: "double", nullable: false),
                    UnlockRound = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_survival_special_wave_monster_types_siege_survival_spe~",
                        column: x => x.SurvivalSpecialWaveProfileId,
                        principalTable: "siege_survival_special_wave_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_survival_mystery_box_entry_enchantments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SurvivalMysteryBoxEntryId = table.Column<int>(type: "int", nullable: false),
                    EnchantmentDefinitionId = table.Column<int>(type: "int", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    ChancePercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_survival_mystery_box_entry_enchantments_EnchantmentDef~",
                        column: x => x.EnchantmentDefinitionId,
                        principalTable: "EnchantmentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_survival_mystery_box_entry_enchantments_siege_survival~",
                        column: x => x.SurvivalMysteryBoxEntryId,
                        principalTable: "siege_survival_mystery_box_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_difficulty_profiles_SiegeScenarioId_SortOrder",
                table: "siege_survival_difficulty_profiles",
                columns: new[] { "SiegeScenarioId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_lobby_settings_SiegeLobbyId",
                table: "siege_survival_lobby_settings",
                column: "SiegeLobbyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_monster_types_SiegeScenarioId_EntityType",
                table: "siege_survival_monster_types",
                columns: new[] { "SiegeScenarioId", "EntityType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_mystery_box_entries_EnchantmentDefinitionId",
                table: "siege_survival_mystery_box_entries",
                column: "EnchantmentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_mystery_box_entries_ItemBlueprintId",
                table: "siege_survival_mystery_box_entries",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_mystery_box_entries_SurvivalMysteryBoxPoolId_~",
                table: "siege_survival_mystery_box_entries",
                columns: new[] { "SurvivalMysteryBoxPoolId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_mystery_box_entry_enchantments_EnchantmentDef~",
                table: "siege_survival_mystery_box_entry_enchantments",
                column: "EnchantmentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_mystery_box_entry_enchantments_SurvivalMyster~",
                table: "siege_survival_mystery_box_entry_enchantments",
                columns: new[] { "SurvivalMysteryBoxEntryId", "EnchantmentDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_mystery_box_points_LocationId",
                table: "siege_survival_mystery_box_points",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_mystery_box_points_SiegeScenarioId_SortOrder",
                table: "siege_survival_mystery_box_points",
                columns: new[] { "SiegeScenarioId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_mystery_box_points_SurvivalMysteryBoxPoolId",
                table: "siege_survival_mystery_box_points",
                column: "SurvivalMysteryBoxPoolId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_mystery_box_pools_SiegeScenarioId",
                table: "siege_survival_mystery_box_pools",
                column: "SiegeScenarioId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_purchase_offers_ItemBlueprintId",
                table: "siege_survival_purchase_offers",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_purchase_offers_SurvivalPurchasePointId_SortO~",
                table: "siege_survival_purchase_offers",
                columns: new[] { "SurvivalPurchasePointId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_purchase_points_LocationId",
                table: "siege_survival_purchase_points",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_purchase_points_SiegeScenarioId_SortOrder",
                table: "siege_survival_purchase_points",
                columns: new[] { "SiegeScenarioId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_settings_BaselineLoadoutKitId",
                table: "siege_survival_settings",
                column: "BaselineLoadoutKitId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_settings_DefaultKitId",
                table: "siege_survival_settings",
                column: "DefaultKitId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_settings_HumanTeamId",
                table: "siege_survival_settings",
                column: "HumanTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_settings_NpcTeamId",
                table: "siege_survival_settings",
                column: "NpcTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_settings_SiegeScenarioId",
                table: "siege_survival_settings",
                column: "SiegeScenarioId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_settings_kits_KitId",
                table: "siege_survival_settings_kits",
                column: "KitId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_spawn_zone_objectives_SiegeObjectiveId",
                table: "siege_survival_spawn_zone_objectives",
                column: "SiegeObjectiveId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_spawn_zone_points_LocationId",
                table: "siege_survival_spawn_zone_points",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_spawn_zone_points_SurvivalSpawnZoneId_Kind_So~",
                table: "siege_survival_spawn_zone_points",
                columns: new[] { "SurvivalSpawnZoneId", "Kind", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_spawn_zones_AnchorLocationId",
                table: "siege_survival_spawn_zones",
                column: "AnchorLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_spawn_zones_SiegeScenarioId_SortOrder",
                table: "siege_survival_spawn_zones",
                columns: new[] { "SiegeScenarioId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_special_wave_entries_SiegeScenarioId_SortOrder",
                table: "siege_survival_special_wave_entries",
                columns: new[] { "SiegeScenarioId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_special_wave_entries_SurvivalSpecialWaveProfi~",
                table: "siege_survival_special_wave_entries",
                column: "SurvivalSpecialWaveProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_special_wave_monster_types_SurvivalSpecialWav~",
                table: "siege_survival_special_wave_monster_types",
                columns: new[] { "SurvivalSpecialWaveProfileId", "EntityType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_special_wave_profiles_SiegeScenarioId_Key",
                table: "siege_survival_special_wave_profiles",
                columns: new[] { "SiegeScenarioId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_target_overrides_GateStructureId",
                table: "siege_survival_target_overrides",
                column: "GateStructureId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_target_overrides_SiegeObjectiveId",
                table: "siege_survival_target_overrides",
                column: "SiegeObjectiveId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_target_overrides_SiegeScenarioId_GateStructur~",
                table: "siege_survival_target_overrides",
                columns: new[] { "SiegeScenarioId", "GateStructureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_target_overrides_SiegeScenarioId_SiegeObjecti~",
                table: "siege_survival_target_overrides",
                columns: new[] { "SiegeScenarioId", "SiegeObjectiveId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "siege_survival_difficulty_profiles");

            migrationBuilder.DropTable(
                name: "siege_survival_lobby_settings");

            migrationBuilder.DropTable(
                name: "siege_survival_monster_types");

            migrationBuilder.DropTable(
                name: "siege_survival_mystery_box_entry_enchantments");

            migrationBuilder.DropTable(
                name: "siege_survival_mystery_box_points");

            migrationBuilder.DropTable(
                name: "siege_survival_purchase_offers");

            migrationBuilder.DropTable(
                name: "siege_survival_settings_kits");

            migrationBuilder.DropTable(
                name: "siege_survival_spawn_zone_objectives");

            migrationBuilder.DropTable(
                name: "siege_survival_spawn_zone_points");

            migrationBuilder.DropTable(
                name: "siege_survival_special_wave_entries");

            migrationBuilder.DropTable(
                name: "siege_survival_special_wave_monster_types");

            migrationBuilder.DropTable(
                name: "siege_survival_target_overrides");

            migrationBuilder.DropTable(
                name: "siege_survival_mystery_box_entries");

            migrationBuilder.DropTable(
                name: "siege_survival_purchase_points");

            migrationBuilder.DropTable(
                name: "siege_survival_settings");

            migrationBuilder.DropTable(
                name: "siege_survival_spawn_zones");

            migrationBuilder.DropTable(
                name: "siege_survival_special_wave_profiles");

            migrationBuilder.DropTable(
                name: "siege_survival_mystery_box_pools");
        }
    }
}
