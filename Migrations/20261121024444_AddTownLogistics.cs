using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddTownLogistics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "consumer_replenishments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    StorageId = table.Column<int>(type: "int", nullable: false),
                    StructureId = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    ReorderPoint = table.Column<long>(type: "bigint", nullable: false),
                    TargetLevel = table.Column<long>(type: "bigint", nullable: false),
                    PriorityClass = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Note = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumer_replenishments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_consumer_replenishments_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_consumer_replenishments_storages_StorageId",
                        column: x => x.StorageId,
                        principalTable: "storages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_consumer_replenishments_structures_StructureId",
                        column: x => x.StructureId,
                        principalTable: "structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "logistics_planner_runs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TownId = table.Column<int>(type: "int", nullable: false),
                    LogisticsPolicyId = table.Column<int>(type: "int", nullable: true),
                    PolicyVersion = table.Column<int>(type: "int", nullable: true),
                    Trigger = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DemandLines = table.Column<int>(type: "int", nullable: false),
                    ReservationsCreated = table.Column<int>(type: "int", nullable: false),
                    OrdersCreated = table.Column<int>(type: "int", nullable: false),
                    ReservationsReleased = table.Column<int>(type: "int", nullable: false),
                    ReservationsCollected = table.Column<int>(type: "int", nullable: false),
                    Shortfalls = table.Column<int>(type: "int", nullable: false),
                    Summary = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_logistics_planner_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_logistics_planner_runs_towns_TownId",
                        column: x => x.TownId,
                        principalTable: "towns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "logistics_policies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Scope = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TownId = table.Column<int>(type: "int", nullable: true),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    Specialization = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TownSize = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsDefaultTemplate = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    LineageId = table.Column<int>(type: "int", nullable: false),
                    CurrentKey = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BasedOnPolicyId = table.Column<int>(type: "int", nullable: true),
                    SupersededByPolicyId = table.Column<int>(type: "int", nullable: true),
                    SupersededAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Author = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_logistics_policies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_logistics_policies_towns_TownId",
                        column: x => x.TownId,
                        principalTable: "towns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_logistics_policies_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "logistics_reservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TownId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    WarehouseStorageId = table.Column<int>(type: "int", nullable: false),
                    ConsumerStructureId = table.Column<int>(type: "int", nullable: false),
                    ConsumerStorageId = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    Collected = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PriorityClass = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OpenKey = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TransportOrderId = table.Column<int>(type: "int", nullable: true),
                    PlannerRunId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Explanation = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StatusReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_logistics_reservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_logistics_reservations_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_logistics_reservations_storages_ConsumerStorageId",
                        column: x => x.ConsumerStorageId,
                        principalTable: "storages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_logistics_reservations_storages_WarehouseStorageId",
                        column: x => x.WarehouseStorageId,
                        principalTable: "storages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_logistics_reservations_structures_ConsumerStructureId",
                        column: x => x.ConsumerStructureId,
                        principalTable: "structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_logistics_reservations_towns_TownId",
                        column: x => x.TownId,
                        principalTable: "towns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_logistics_reservations_transport_orders_TransportOrderId",
                        column: x => x.TransportOrderId,
                        principalTable: "transport_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_logistics_reservations_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "logistics_plan_entries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    LogisticsPlannerRunId = table.Column<int>(type: "int", nullable: false),
                    ConsumerStructureId = table.Column<int>(type: "int", nullable: false),
                    ConsumerStorageId = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    PriorityClass = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Stock = table.Column<long>(type: "bigint", nullable: false),
                    ReorderPoint = table.Column<long>(type: "bigint", nullable: false),
                    TargetLevel = table.Column<long>(type: "bigint", nullable: false),
                    Shortfall = table.Column<long>(type: "bigint", nullable: false),
                    Reserved = table.Column<long>(type: "bigint", nullable: false),
                    Outcome = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RuleIds = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LogisticsReservationId = table.Column<int>(type: "int", nullable: true),
                    TransportOrderId = table.Column<int>(type: "int", nullable: true),
                    Explanation = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_logistics_plan_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_logistics_plan_entries_logistics_planner_runs_LogisticsPlann~",
                        column: x => x.LogisticsPlannerRunId,
                        principalTable: "logistics_planner_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "logistics_rules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    LogisticsPolicyId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Scope = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    TargetKind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    TagId = table.Column<int>(type: "int", nullable: true),
                    ConsumerKind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StructureId = table.Column<int>(type: "int", nullable: true),
                    ProductionKindId = table.Column<int>(type: "int", nullable: true),
                    AreaKind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DistrictId = table.Column<int>(type: "int", nullable: true),
                    Units = table.Column<long>(type: "bigint", nullable: true),
                    DaysOfCover = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    ReorderPoint = table.Column<long>(type: "bigint", nullable: true),
                    TargetLevel = table.Column<long>(type: "bigint", nullable: true),
                    PriorityClass = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActiveFrom = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ActiveUntil = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Author = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reason = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_logistics_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_logistics_rules_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_logistics_rules_districts_DistrictId",
                        column: x => x.DistrictId,
                        principalTable: "districts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_logistics_rules_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_logistics_rules_logistics_policies_LogisticsPolicyId",
                        column: x => x.LogisticsPolicyId,
                        principalTable: "logistics_policies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_logistics_rules_production_kinds_ProductionKindId",
                        column: x => x.ProductionKindId,
                        principalTable: "production_kinds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_logistics_rules_structures_StructureId",
                        column: x => x.StructureId,
                        principalTable: "structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_logistics_rules_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_logistics_rules_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_consumer_replenishments_ItemBlueprintId",
                table: "consumer_replenishments",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_consumer_replenishments_StorageId_ItemBlueprintId",
                table: "consumer_replenishments",
                columns: new[] { "StorageId", "ItemBlueprintId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_consumer_replenishments_StructureId",
                table: "consumer_replenishments",
                column: "StructureId");

            migrationBuilder.CreateIndex(
                name: "IX_logistics_plan_entries_ConsumerStructureId_ItemBlueprintId",
                table: "logistics_plan_entries",
                columns: new[] { "ConsumerStructureId", "ItemBlueprintId" });

            migrationBuilder.CreateIndex(
                name: "IX_logistics_plan_entries_LogisticsPlannerRunId",
                table: "logistics_plan_entries",
                column: "LogisticsPlannerRunId");

            migrationBuilder.CreateIndex(
                name: "IX_logistics_planner_runs_StartedAt",
                table: "logistics_planner_runs",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_logistics_planner_runs_TownId_StartedAt",
                table: "logistics_planner_runs",
                columns: new[] { "TownId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_logistics_policies_CurrentKey",
                table: "logistics_policies",
                column: "CurrentKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_logistics_policies_LineageId_Version",
                table: "logistics_policies",
                columns: new[] { "LineageId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_logistics_policies_Scope_TownId",
                table: "logistics_policies",
                columns: new[] { "Scope", "TownId" });

            migrationBuilder.CreateIndex(
                name: "IX_logistics_policies_TownId",
                table: "logistics_policies",
                column: "TownId");

            migrationBuilder.CreateIndex(
                name: "IX_logistics_policies_WarehouseId",
                table: "logistics_policies",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_logistics_reservations_ConsumerStorageId",
                table: "logistics_reservations",
                column: "ConsumerStorageId");

            migrationBuilder.CreateIndex(
                name: "IX_logistics_reservations_ConsumerStructureId_Status",
                table: "logistics_reservations",
                columns: new[] { "ConsumerStructureId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_logistics_reservations_ItemBlueprintId",
                table: "logistics_reservations",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_logistics_reservations_OpenKey",
                table: "logistics_reservations",
                column: "OpenKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_logistics_reservations_TownId_Status",
                table: "logistics_reservations",
                columns: new[] { "TownId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_logistics_reservations_TransportOrderId",
                table: "logistics_reservations",
                column: "TransportOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_logistics_reservations_WarehouseId",
                table: "logistics_reservations",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_logistics_reservations_WarehouseStorageId_ItemBlueprintId_St~",
                table: "logistics_reservations",
                columns: new[] { "WarehouseStorageId", "ItemBlueprintId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_logistics_rules_CategoryId",
                table: "logistics_rules",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_logistics_rules_DistrictId",
                table: "logistics_rules",
                column: "DistrictId");

            migrationBuilder.CreateIndex(
                name: "IX_logistics_rules_ItemBlueprintId",
                table: "logistics_rules",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_logistics_rules_LogisticsPolicyId_Sequence",
                table: "logistics_rules",
                columns: new[] { "LogisticsPolicyId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_logistics_rules_ProductionKindId",
                table: "logistics_rules",
                column: "ProductionKindId");

            migrationBuilder.CreateIndex(
                name: "IX_logistics_rules_StructureId",
                table: "logistics_rules",
                column: "StructureId");

            migrationBuilder.CreateIndex(
                name: "IX_logistics_rules_TagId",
                table: "logistics_rules",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_logistics_rules_WarehouseId",
                table: "logistics_rules",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumer_replenishments");

            migrationBuilder.DropTable(
                name: "logistics_plan_entries");

            migrationBuilder.DropTable(
                name: "logistics_reservations");

            migrationBuilder.DropTable(
                name: "logistics_rules");

            migrationBuilder.DropTable(
                name: "logistics_planner_runs");

            migrationBuilder.DropTable(
                name: "logistics_policies");
        }
    }
}
