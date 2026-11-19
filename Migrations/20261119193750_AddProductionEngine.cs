using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "climate_zones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_climate_zones", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "production_kinds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Icon = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AreaModel = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExclusiveAreas = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AllowsDerived = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AllowsConfigured = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AllowsHybrid = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ResourceCategory = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DefaultWorkers = table.Column<int>(type: "int", nullable: false),
                    CycleVariation = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DefaultStoragesJson = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_kinds", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "production_lines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProductionStructureId = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    RateSource = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConfiguredRatePerHour = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ResourceCategory = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StatusReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Accrued = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    AnchorAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastSettledAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    NextSettleAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_production_lines_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_lines_production_structures_ProductionStructureId",
                        column: x => x.ProductionStructureId,
                        principalTable: "production_structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "weather_types",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_weather_types", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "climate_zone_towns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ClimateZoneId = table.Column<int>(type: "int", nullable: false),
                    TownId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_climate_zone_towns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_climate_zone_towns_climate_zones_ClimateZoneId",
                        column: x => x.ClimateZoneId,
                        principalTable: "climate_zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_climate_zone_towns_towns_TownId",
                        column: x => x.TownId,
                        principalTable: "towns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "production_kind_levels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProductionKindId = table.Column<int>(type: "int", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    YieldMultiplier = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    CycleSpeedMultiplier = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    MaxAreaSize = table.Column<int>(type: "int", nullable: true),
                    OutputMaxUnits = table.Column<int>(type: "int", nullable: true),
                    UnitsPerSlot = table.Column<int>(type: "int", nullable: true),
                    TransportCapacity = table.Column<int>(type: "int", nullable: true),
                    UpkeepReduction = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    UpgradeCoins = table.Column<long>(type: "bigint", nullable: false),
                    UpgradeDurationMinutes = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_kind_levels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_production_kind_levels_production_kinds_ProductionKindId",
                        column: x => x.ProductionKindId,
                        principalTable: "production_kinds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "production_modifiers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Scope = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ScopeRefId = table.Column<int>(type: "int", nullable: true),
                    Target = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TargetItemBlueprintId = table.Column<int>(type: "int", nullable: true),
                    TargetCategory = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TargetProductionKindId = table.Column<int>(type: "int", nullable: true),
                    Effect = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ActiveFrom = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ActiveUntil = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_modifiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_production_modifiers_item_blueprints_TargetItemBlueprintId",
                        column: x => x.TargetItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_modifiers_production_kinds_TargetProductionKindId",
                        column: x => x.TargetProductionKindId,
                        principalTable: "production_kinds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "production_cycle_records",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProductionLineId = table.Column<int>(type: "int", nullable: false),
                    ProductionStructureId = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    SettledAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ElapsedSeconds = table.Column<long>(type: "bigint", nullable: false),
                    SkippedSeconds = table.Column<long>(type: "bigint", nullable: false),
                    RateSource = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BaseRatePerHour = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ModifiersJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UnlockFactor = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    WorkersPresent = table.Column<int>(type: "int", nullable: false),
                    WorkersTotal = table.Column<int>(type: "int", nullable: false),
                    WorkerFactor = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    EffectiveRatePerHour = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    AccruedBefore = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    AccruedAfter = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Emitted = table.Column<long>(type: "bigint", nullable: false),
                    Accepted = table.Column<long>(type: "bigint", nullable: false),
                    InputsJson = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StatusAfter = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ShortfallReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StockTransactionId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_cycle_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_production_cycle_records_production_lines_ProductionLineId",
                        column: x => x.ProductionLineId,
                        principalTable: "production_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "production_line_inputs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProductionLineId = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    AmountPerHour = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Accrued = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_line_inputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_production_line_inputs_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_line_inputs_production_lines_ProductionLineId",
                        column: x => x.ProductionLineId,
                        principalTable: "production_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "climate_zone_weathers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ClimateZoneId = table.Column<int>(type: "int", nullable: false),
                    WeatherTypeId = table.Column<int>(type: "int", nullable: false),
                    ChancePercent = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_climate_zone_weathers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_climate_zone_weathers_climate_zones_ClimateZoneId",
                        column: x => x.ClimateZoneId,
                        principalTable: "climate_zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_climate_zone_weathers_weather_types_WeatherTypeId",
                        column: x => x.WeatherTypeId,
                        principalTable: "weather_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "weather_rolls",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ClimateZoneId = table.Column<int>(type: "int", nullable: false),
                    Day = table.Column<long>(type: "bigint", nullable: false),
                    DayStartsAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DayEndsAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    WeatherTypeId = table.Column<int>(type: "int", nullable: true),
                    RollValue = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    RolledAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_weather_rolls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_weather_rolls_climate_zones_ClimateZoneId",
                        column: x => x.ClimateZoneId,
                        principalTable: "climate_zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_weather_rolls_weather_types_WeatherTypeId",
                        column: x => x.WeatherTypeId,
                        principalTable: "weather_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "weather_type_effects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    WeatherTypeId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Effect = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_weather_type_effects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_weather_type_effects_weather_types_WeatherTypeId",
                        column: x => x.WeatherTypeId,
                        principalTable: "weather_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "production_kind_level_items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProductionKindLevelId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_kind_level_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_production_kind_level_items_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_kind_level_items_production_kind_levels_Productio~",
                        column: x => x.ProductionKindLevelId,
                        principalTable: "production_kind_levels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.InsertData(
                table: "climate_zones",
                columns: new[] { "Id", "CreatedAt", "Description", "IsDefault", "Name" },
                values: new object[] { 1, new DateTime(2026, 11, 19, 0, 0, 0, 0, DateTimeKind.Utc), "Every town without a zone of its own. No weather odds until staff set them, so the weather is clear.", true, "Default climate" });

            migrationBuilder.InsertData(
                table: "production_kinds",
                columns: new[] { "Id", "AllowsConfigured", "AllowsDerived", "AllowsHybrid", "AreaModel", "CreatedAt", "CycleVariation", "DefaultStoragesJson", "DefaultWorkers", "Description", "ExclusiveAreas", "Icon", "IsActive", "Name", "ResourceCategory" },
                values: new object[,]
                {
                    { 1, true, true, true, "BorderedField", new DateTime(2026, 11, 19, 0, 0, 0, 0, DateTimeKind.Utc), false, null, 2, "Crops on bordered farmland; the crop mix follows the field.", true, "WHEAT", true, "Farm", "Crops" },
                    { 2, true, true, true, "SharedRadius", new DateTime(2026, 11, 19, 0, 0, 0, 0, DateTimeKind.Utc), false, null, 2, "Trees in a shared radius around the structure; the yield follows the forest.", false, "IRON_AXE", true, "Logger", "Wood" },
                    { 3, true, true, true, "ExclusiveVolume", new DateTime(2026, 11, 19, 0, 0, 0, 0, DateTimeKind.Utc), false, null, 2, "Rock types in an exclusive volume; ores are byproducts.", true, "STONE", true, "Quarry", "Stone" },
                    { 4, true, true, true, "ExclusiveVolume", new DateTime(2026, 11, 19, 0, 0, 0, 0, DateTimeKind.Utc), false, null, 2, "Ore in an exclusive mineshaft; the designed ore gets the primary boost.", true, "IRON_PICKAXE", true, "Mine", "Ore" },
                    { 5, true, true, true, "SharedRadius", new DateTime(2026, 11, 19, 0, 0, 0, 0, DateTimeKind.Utc), false, null, 2, "Fish from the water in a shared radius, by biome.", false, "FISHING_ROD", true, "Fishery", "Fish" },
                    { 6, true, true, true, "BorderedField", new DateTime(2026, 11, 19, 0, 0, 0, 0, DateTimeKind.Utc), false, null, 2, "Flowers in bordered beds.", true, "POPPY", true, "Flower farm", "Flowers" },
                    { 7, true, true, true, "SharedRadius", new DateTime(2026, 11, 19, 0, 0, 0, 0, DateTimeKind.Utc), false, null, 2, "Flowers growing in a shared radius.", false, "SUNFLOWER", true, "Florist", "Flowers" },
                    { 8, true, true, true, "Pasture", new DateTime(2026, 11, 19, 0, 0, 0, 0, DateTimeKind.Utc), false, null, 2, "Drops of the animals in a pasture.", true, "WHITE_WOOL", true, "Animal farm", "Animals" }
                });

            migrationBuilder.InsertData(
                table: "weather_types",
                columns: new[] { "Id", "CreatedAt", "Description", "Name" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 11, 19, 0, 0, 0, 0, DateTimeKind.Utc), "Good for crops and flowers.", "Rain" },
                    { 2, new DateTime(2026, 11, 19, 0, 0, 0, 0, DateTimeKind.Utc), "Hits fisheries hard and farms a little.", "Storm" },
                    { 3, new DateTime(2026, 11, 19, 0, 0, 0, 0, DateTimeKind.Utc), "Hits farms, flower farms and pastures.", "Drought" }
                });

            migrationBuilder.InsertData(
                table: "production_kind_levels",
                columns: new[] { "Id", "CycleSpeedMultiplier", "Level", "MaxAreaSize", "OutputMaxUnits", "ProductionKindId", "TransportCapacity", "UnitsPerSlot", "UpgradeCoins", "UpgradeDurationMinutes", "UpkeepReduction", "YieldMultiplier" },
                values: new object[,]
                {
                    { 1, 1.00m, 1, 1024, null, 1, null, null, 0L, null, 0m, 1.00m },
                    { 2, 1.05m, 2, 1280, null, 1, null, null, 250L, null, 0m, 1.15m },
                    { 3, 1.10m, 3, 1536, null, 1, null, null, 1000L, null, 0m, 1.30m },
                    { 4, 1.15m, 4, 1792, null, 1, null, null, 2250L, null, 0m, 1.45m },
                    { 5, 1.20m, 5, 2048, null, 1, null, null, 4000L, null, 0m, 1.60m },
                    { 6, 1.25m, 6, 2304, null, 1, null, null, 6250L, null, 0m, 1.75m },
                    { 7, 1.30m, 7, 2560, null, 1, null, null, 9000L, null, 0m, 1.90m },
                    { 8, 1.35m, 8, 2816, null, 1, null, null, 12250L, null, 0m, 2.05m },
                    { 9, 1.40m, 9, 3072, null, 1, null, null, 16000L, null, 0m, 2.20m },
                    { 10, 1.45m, 10, 3328, null, 1, null, null, 20250L, null, 0m, 2.35m },
                    { 11, 1.00m, 1, 32, null, 2, null, null, 0L, null, 0m, 1.00m },
                    { 12, 1.05m, 2, 36, null, 2, null, null, 250L, null, 0m, 1.15m },
                    { 13, 1.10m, 3, 40, null, 2, null, null, 1000L, null, 0m, 1.30m },
                    { 14, 1.15m, 4, 44, null, 2, null, null, 2250L, null, 0m, 1.45m },
                    { 15, 1.20m, 5, 48, null, 2, null, null, 4000L, null, 0m, 1.60m },
                    { 16, 1.25m, 6, 52, null, 2, null, null, 6250L, null, 0m, 1.75m },
                    { 17, 1.30m, 7, 56, null, 2, null, null, 9000L, null, 0m, 1.90m },
                    { 18, 1.35m, 8, 60, null, 2, null, null, 12250L, null, 0m, 2.05m },
                    { 19, 1.40m, 9, 64, null, 2, null, null, 16000L, null, 0m, 2.20m },
                    { 20, 1.45m, 10, 68, null, 2, null, null, 20250L, null, 0m, 2.35m },
                    { 21, 1.00m, 1, 262144, null, 3, null, null, 0L, null, 0m, 1.00m },
                    { 22, 1.05m, 2, 327680, null, 3, null, null, 250L, null, 0m, 1.15m },
                    { 23, 1.10m, 3, 393216, null, 3, null, null, 1000L, null, 0m, 1.30m },
                    { 24, 1.15m, 4, 458752, null, 3, null, null, 2250L, null, 0m, 1.45m },
                    { 25, 1.20m, 5, 524288, null, 3, null, null, 4000L, null, 0m, 1.60m },
                    { 26, 1.25m, 6, 589824, null, 3, null, null, 6250L, null, 0m, 1.75m },
                    { 27, 1.30m, 7, 655360, null, 3, null, null, 9000L, null, 0m, 1.90m },
                    { 28, 1.35m, 8, 720896, null, 3, null, null, 12250L, null, 0m, 2.05m },
                    { 29, 1.40m, 9, 786432, null, 3, null, null, 16000L, null, 0m, 2.20m },
                    { 30, 1.45m, 10, 851968, null, 3, null, null, 20250L, null, 0m, 2.35m },
                    { 31, 1.00m, 1, 262144, null, 4, null, null, 0L, null, 0m, 1.00m },
                    { 32, 1.05m, 2, 327680, null, 4, null, null, 250L, null, 0m, 1.15m },
                    { 33, 1.10m, 3, 393216, null, 4, null, null, 1000L, null, 0m, 1.30m },
                    { 34, 1.15m, 4, 458752, null, 4, null, null, 2250L, null, 0m, 1.45m },
                    { 35, 1.20m, 5, 524288, null, 4, null, null, 4000L, null, 0m, 1.60m },
                    { 36, 1.25m, 6, 589824, null, 4, null, null, 6250L, null, 0m, 1.75m },
                    { 37, 1.30m, 7, 655360, null, 4, null, null, 9000L, null, 0m, 1.90m },
                    { 38, 1.35m, 8, 720896, null, 4, null, null, 12250L, null, 0m, 2.05m },
                    { 39, 1.40m, 9, 786432, null, 4, null, null, 16000L, null, 0m, 2.20m },
                    { 40, 1.45m, 10, 851968, null, 4, null, null, 20250L, null, 0m, 2.35m },
                    { 41, 1.00m, 1, 32, null, 5, null, null, 0L, null, 0m, 1.00m },
                    { 42, 1.05m, 2, 36, null, 5, null, null, 250L, null, 0m, 1.15m },
                    { 43, 1.10m, 3, 40, null, 5, null, null, 1000L, null, 0m, 1.30m },
                    { 44, 1.15m, 4, 44, null, 5, null, null, 2250L, null, 0m, 1.45m },
                    { 45, 1.20m, 5, 48, null, 5, null, null, 4000L, null, 0m, 1.60m },
                    { 46, 1.25m, 6, 52, null, 5, null, null, 6250L, null, 0m, 1.75m },
                    { 47, 1.30m, 7, 56, null, 5, null, null, 9000L, null, 0m, 1.90m },
                    { 48, 1.35m, 8, 60, null, 5, null, null, 12250L, null, 0m, 2.05m },
                    { 49, 1.40m, 9, 64, null, 5, null, null, 16000L, null, 0m, 2.20m },
                    { 50, 1.45m, 10, 68, null, 5, null, null, 20250L, null, 0m, 2.35m },
                    { 51, 1.00m, 1, 1024, null, 6, null, null, 0L, null, 0m, 1.00m },
                    { 52, 1.05m, 2, 1280, null, 6, null, null, 250L, null, 0m, 1.15m },
                    { 53, 1.10m, 3, 1536, null, 6, null, null, 1000L, null, 0m, 1.30m },
                    { 54, 1.15m, 4, 1792, null, 6, null, null, 2250L, null, 0m, 1.45m },
                    { 55, 1.20m, 5, 2048, null, 6, null, null, 4000L, null, 0m, 1.60m },
                    { 56, 1.25m, 6, 2304, null, 6, null, null, 6250L, null, 0m, 1.75m },
                    { 57, 1.30m, 7, 2560, null, 6, null, null, 9000L, null, 0m, 1.90m },
                    { 58, 1.35m, 8, 2816, null, 6, null, null, 12250L, null, 0m, 2.05m },
                    { 59, 1.40m, 9, 3072, null, 6, null, null, 16000L, null, 0m, 2.20m },
                    { 60, 1.45m, 10, 3328, null, 6, null, null, 20250L, null, 0m, 2.35m },
                    { 61, 1.00m, 1, 32, null, 7, null, null, 0L, null, 0m, 1.00m },
                    { 62, 1.05m, 2, 36, null, 7, null, null, 250L, null, 0m, 1.15m },
                    { 63, 1.10m, 3, 40, null, 7, null, null, 1000L, null, 0m, 1.30m },
                    { 64, 1.15m, 4, 44, null, 7, null, null, 2250L, null, 0m, 1.45m },
                    { 65, 1.20m, 5, 48, null, 7, null, null, 4000L, null, 0m, 1.60m },
                    { 66, 1.25m, 6, 52, null, 7, null, null, 6250L, null, 0m, 1.75m },
                    { 67, 1.30m, 7, 56, null, 7, null, null, 9000L, null, 0m, 1.90m },
                    { 68, 1.35m, 8, 60, null, 7, null, null, 12250L, null, 0m, 2.05m },
                    { 69, 1.40m, 9, 64, null, 7, null, null, 16000L, null, 0m, 2.20m },
                    { 70, 1.45m, 10, 68, null, 7, null, null, 20250L, null, 0m, 2.35m },
                    { 71, 1.00m, 1, 1024, null, 8, null, null, 0L, null, 0m, 1.00m },
                    { 72, 1.05m, 2, 1280, null, 8, null, null, 250L, null, 0m, 1.15m },
                    { 73, 1.10m, 3, 1536, null, 8, null, null, 1000L, null, 0m, 1.30m },
                    { 74, 1.15m, 4, 1792, null, 8, null, null, 2250L, null, 0m, 1.45m },
                    { 75, 1.20m, 5, 2048, null, 8, null, null, 4000L, null, 0m, 1.60m },
                    { 76, 1.25m, 6, 2304, null, 8, null, null, 6250L, null, 0m, 1.75m },
                    { 77, 1.30m, 7, 2560, null, 8, null, null, 9000L, null, 0m, 1.90m },
                    { 78, 1.35m, 8, 2816, null, 8, null, null, 12250L, null, 0m, 2.05m },
                    { 79, 1.40m, 9, 3072, null, 8, null, null, 16000L, null, 0m, 2.20m },
                    { 80, 1.45m, 10, 3328, null, 8, null, null, 20250L, null, 0m, 2.35m }
                });

            migrationBuilder.InsertData(
                table: "weather_type_effects",
                columns: new[] { "Id", "Category", "Effect", "Value", "WeatherTypeId" },
                values: new object[,]
                {
                    { 1, "Crops", "Multiply", 1.1m, 1 },
                    { 2, "Flowers", "Multiply", 1.1m, 1 },
                    { 3, "Fish", "Multiply", 0.5m, 2 },
                    { 4, "Crops", "Multiply", 0.9m, 2 },
                    { 5, "Crops", "Multiply", 0.6m, 3 },
                    { 6, "Flowers", "Multiply", 0.7m, 3 },
                    { 7, "Animals", "Multiply", 0.8m, 3 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_climate_zone_towns_ClimateZoneId",
                table: "climate_zone_towns",
                column: "ClimateZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_climate_zone_towns_TownId",
                table: "climate_zone_towns",
                column: "TownId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_climate_zone_weathers_ClimateZoneId_WeatherTypeId",
                table: "climate_zone_weathers",
                columns: new[] { "ClimateZoneId", "WeatherTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_climate_zone_weathers_WeatherTypeId",
                table: "climate_zone_weathers",
                column: "WeatherTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_climate_zones_Name",
                table: "climate_zones",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_production_cycle_records_ProductionLineId_SettledAt",
                table: "production_cycle_records",
                columns: new[] { "ProductionLineId", "SettledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_production_cycle_records_ProductionStructureId_SettledAt",
                table: "production_cycle_records",
                columns: new[] { "ProductionStructureId", "SettledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_production_kind_level_items_ItemBlueprintId",
                table: "production_kind_level_items",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_production_kind_level_items_ProductionKindLevelId_Role_ItemB~",
                table: "production_kind_level_items",
                columns: new[] { "ProductionKindLevelId", "Role", "ItemBlueprintId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_production_kind_levels_ProductionKindId_Level",
                table: "production_kind_levels",
                columns: new[] { "ProductionKindId", "Level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_production_kinds_Name",
                table: "production_kinds",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_production_line_inputs_ItemBlueprintId",
                table: "production_line_inputs",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_production_line_inputs_ProductionLineId_ItemBlueprintId",
                table: "production_line_inputs",
                columns: new[] { "ProductionLineId", "ItemBlueprintId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_production_lines_ItemBlueprintId",
                table: "production_lines",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_production_lines_NextSettleAt",
                table: "production_lines",
                column: "NextSettleAt");

            migrationBuilder.CreateIndex(
                name: "IX_production_lines_ProductionStructureId_ItemBlueprintId",
                table: "production_lines",
                columns: new[] { "ProductionStructureId", "ItemBlueprintId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_production_modifiers_Enabled_Scope",
                table: "production_modifiers",
                columns: new[] { "Enabled", "Scope" });

            migrationBuilder.CreateIndex(
                name: "IX_production_modifiers_TargetItemBlueprintId",
                table: "production_modifiers",
                column: "TargetItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_production_modifiers_TargetProductionKindId",
                table: "production_modifiers",
                column: "TargetProductionKindId");

            migrationBuilder.CreateIndex(
                name: "IX_weather_rolls_ClimateZoneId_Day",
                table: "weather_rolls",
                columns: new[] { "ClimateZoneId", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_weather_rolls_WeatherTypeId",
                table: "weather_rolls",
                column: "WeatherTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_weather_type_effects_WeatherTypeId_Category",
                table: "weather_type_effects",
                columns: new[] { "WeatherTypeId", "Category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_weather_types_Name",
                table: "weather_types",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_production_structures_production_kinds_ProductionKindId",
                table: "production_structures",
                column: "ProductionKindId",
                principalTable: "production_kinds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_production_structures_production_kinds_ProductionKindId",
                table: "production_structures");

            migrationBuilder.DropTable(
                name: "climate_zone_towns");

            migrationBuilder.DropTable(
                name: "climate_zone_weathers");

            migrationBuilder.DropTable(
                name: "production_cycle_records");

            migrationBuilder.DropTable(
                name: "production_kind_level_items");

            migrationBuilder.DropTable(
                name: "production_line_inputs");

            migrationBuilder.DropTable(
                name: "production_modifiers");

            migrationBuilder.DropTable(
                name: "weather_rolls");

            migrationBuilder.DropTable(
                name: "weather_type_effects");

            migrationBuilder.DropTable(
                name: "production_kind_levels");

            migrationBuilder.DropTable(
                name: "production_lines");

            migrationBuilder.DropTable(
                name: "climate_zones");

            migrationBuilder.DropTable(
                name: "weather_types");

            migrationBuilder.DropTable(
                name: "production_kinds");
        }
    }
}
