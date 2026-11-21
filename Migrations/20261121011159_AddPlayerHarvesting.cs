using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerHarvesting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PoolFloor",
                table: "production_kinds",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 0.5m);

            migrationBuilder.AddColumn<decimal>(
                name: "PoolFactor",
                table: "production_cycle_records",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.CreateTable(
                name: "harvest_rules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProductionKindId = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TargetKind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TargetsJson = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MinBlockAge = table.Column<int>(type: "int", nullable: true),
                    RequiredPermission = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MinTitleBracketId = table.Column<int>(type: "int", nullable: true),
                    ToolClass = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MinToolTier = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    YieldItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    YieldPerBreak = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    FortuneBonusPerLevel = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    WorkerShare = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    PoolCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    CooldownSeconds = table.Column<int>(type: "int", nullable: true),
                    Placeholder = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PlaceholderMaterial = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RegenSeconds = table.Column<int>(type: "int", nullable: false),
                    RegenScalesWithPool = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_harvest_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_harvest_rules_item_blueprints_YieldItemBlueprintId",
                        column: x => x.YieldItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_harvest_rules_production_kinds_ProductionKindId",
                        column: x => x.ProductionKindId,
                        principalTable: "production_kinds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_harvest_rules_title_brackets_MinTitleBracketId",
                        column: x => x.MinTitleBracketId,
                        principalTable: "title_brackets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "resource_pools",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProductionAreaId = table.Column<int>(type: "int", nullable: false),
                    ProductionStructureId = table.Column<int>(type: "int", nullable: false),
                    Capacity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Current = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    RegenPerHour = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    CapacityFromSurvey = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CapacitySurveyId = table.Column<long>(type: "bigint", nullable: true),
                    LastRegenAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resource_pools", x => x.Id);
                    table.ForeignKey(
                        name: "FK_resource_pools_production_areas_ProductionAreaId",
                        column: x => x.ProductionAreaId,
                        principalTable: "production_areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "harvest_accruals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    HarvestRuleId = table.Column<int>(type: "int", nullable: false),
                    WorkerAccrued = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    TownAccrued = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_harvest_accruals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_harvest_accruals_harvest_rules_HarvestRuleId",
                        column: x => x.HarvestRuleId,
                        principalTable: "harvest_rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "player_harvests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    IdempotencyKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestHash = table.Column<string>(type: "char(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    HarvestRuleId = table.Column<int>(type: "int", nullable: false),
                    ProductionAreaId = table.Column<int>(type: "int", nullable: false),
                    ProductionStructureId = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    Breaks = table.Column<int>(type: "int", nullable: false),
                    FortuneLevel = table.Column<int>(type: "int", nullable: false),
                    HarvestedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    YieldUnits = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    WorkerShare = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    WorkerUnits = table.Column<long>(type: "bigint", nullable: false),
                    TownUnits = table.Column<long>(type: "bigint", nullable: false),
                    TownAccepted = table.Column<long>(type: "bigint", nullable: false),
                    PoolBefore = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    PoolAfter = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    PoolCapacity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    RegenSeconds = table.Column<int>(type: "int", nullable: false),
                    WorkerTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    TownTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    ShortfallReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_harvests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_player_harvests_harvest_rules_HarvestRuleId",
                        column: x => x.HarvestRuleId,
                        principalTable: "harvest_rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_player_harvests_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.UpdateData(
                table: "production_kinds",
                keyColumn: "Id",
                keyValue: 1,
                column: "PoolFloor",
                value: 0.5m);

            migrationBuilder.UpdateData(
                table: "production_kinds",
                keyColumn: "Id",
                keyValue: 2,
                column: "PoolFloor",
                value: 0.5m);

            migrationBuilder.UpdateData(
                table: "production_kinds",
                keyColumn: "Id",
                keyValue: 3,
                column: "PoolFloor",
                value: 0.5m);

            migrationBuilder.UpdateData(
                table: "production_kinds",
                keyColumn: "Id",
                keyValue: 4,
                column: "PoolFloor",
                value: 0.5m);

            migrationBuilder.UpdateData(
                table: "production_kinds",
                keyColumn: "Id",
                keyValue: 5,
                column: "PoolFloor",
                value: 0.5m);

            migrationBuilder.UpdateData(
                table: "production_kinds",
                keyColumn: "Id",
                keyValue: 6,
                column: "PoolFloor",
                value: 0.5m);

            migrationBuilder.UpdateData(
                table: "production_kinds",
                keyColumn: "Id",
                keyValue: 7,
                column: "PoolFloor",
                value: 0.5m);

            migrationBuilder.UpdateData(
                table: "production_kinds",
                keyColumn: "Id",
                keyValue: 8,
                column: "PoolFloor",
                value: 0.5m);

            migrationBuilder.CreateIndex(
                name: "IX_harvest_accruals_HarvestRuleId",
                table: "harvest_accruals",
                column: "HarvestRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_harvest_accruals_UserId_HarvestRuleId",
                table: "harvest_accruals",
                columns: new[] { "UserId", "HarvestRuleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_harvest_rules_MinTitleBracketId",
                table: "harvest_rules",
                column: "MinTitleBracketId");

            migrationBuilder.CreateIndex(
                name: "IX_harvest_rules_ProductionKindId_Name",
                table: "harvest_rules",
                columns: new[] { "ProductionKindId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_harvest_rules_YieldItemBlueprintId",
                table: "harvest_rules",
                column: "YieldItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_player_harvests_HarvestRuleId",
                table: "player_harvests",
                column: "HarvestRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_player_harvests_IdempotencyKey",
                table: "player_harvests",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_player_harvests_ProductionAreaId_ReceivedAt",
                table: "player_harvests",
                columns: new[] { "ProductionAreaId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_player_harvests_ProductionStructureId_ReceivedAt",
                table: "player_harvests",
                columns: new[] { "ProductionStructureId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_player_harvests_UserId_ReceivedAt",
                table: "player_harvests",
                columns: new[] { "UserId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_resource_pools_ProductionAreaId",
                table: "resource_pools",
                column: "ProductionAreaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_resource_pools_ProductionStructureId",
                table: "resource_pools",
                column: "ProductionStructureId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "harvest_accruals");

            migrationBuilder.DropTable(
                name: "player_harvests");

            migrationBuilder.DropTable(
                name: "resource_pools");

            migrationBuilder.DropTable(
                name: "harvest_rules");

            migrationBuilder.DropColumn(
                name: "PoolFloor",
                table: "production_kinds");

            migrationBuilder.DropColumn(
                name: "PoolFactor",
                table: "production_cycle_records");
        }
    }
}
