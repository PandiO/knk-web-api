using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddTransportCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transport_lanes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    FromStructureId = table.Column<int>(type: "int", nullable: false),
                    ToStructureId = table.Column<int>(type: "int", nullable: false),
                    Reachable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RoadDistance = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    StraightDistance = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    RouteEdgeIdsJson = table.Column<string>(type: "varchar(8000)", maxLength: 8000, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MeasuredSeconds = table.Column<int>(type: "int", nullable: true),
                    Source = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MeasuredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Note = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transport_lanes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transport_lanes_structures_FromStructureId",
                        column: x => x.FromStructureId,
                        principalTable: "structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_transport_lanes_structures_ToStructureId",
                        column: x => x.ToStructureId,
                        principalTable: "structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "transport_orders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Origin = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StatusReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceStorageId = table.Column<int>(type: "int", nullable: false),
                    SourceStructureId = table.Column<int>(type: "int", nullable: true),
                    CargoStorageId = table.Column<int>(type: "int", nullable: false),
                    CarrierStructureId = table.Column<int>(type: "int", nullable: true),
                    CarrierCapacity = table.Column<long>(type: "bigint", nullable: true),
                    Risk = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    CurrentStructureId = table.Column<int>(type: "int", nullable: true),
                    CurrentStopId = table.Column<int>(type: "int", nullable: true),
                    Reroutes = table.Column<int>(type: "int", nullable: false),
                    ReturnAttempts = table.Column<int>(type: "int", nullable: false),
                    CancelRequested = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ExecuteAfter = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    NextActionAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DepartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LegStartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EtaAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    WaitingSince = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClientKey = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transport_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transport_orders_production_structures_CarrierStructureId",
                        column: x => x.CarrierStructureId,
                        principalTable: "production_structures",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_transport_orders_storages_CargoStorageId",
                        column: x => x.CargoStorageId,
                        principalTable: "storages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transport_orders_storages_SourceStorageId",
                        column: x => x.SourceStorageId,
                        principalTable: "storages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transport_orders_structures_SourceStructureId",
                        column: x => x.SourceStructureId,
                        principalTable: "structures",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "transport_assignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransportOrderId = table.Column<int>(type: "int", nullable: false),
                    Mode = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CarrierStructureId = table.Column<int>(type: "int", nullable: true),
                    Workers = table.Column<int>(type: "int", nullable: false),
                    SpeedBlocksPerSecond = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Capacity = table.Column<long>(type: "bigint", nullable: true),
                    AssignedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transport_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transport_assignments_production_structures_CarrierStructure~",
                        column: x => x.CarrierStructureId,
                        principalTable: "production_structures",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_transport_assignments_transport_orders_TransportOrderId",
                        column: x => x.TransportOrderId,
                        principalTable: "transport_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "transport_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransportOrderId = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Kind = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StatusAfter = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TransportStopId = table.Column<int>(type: "int", nullable: true),
                    StockTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    Message = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transport_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transport_events_transport_orders_TransportOrderId",
                        column: x => x.TransportOrderId,
                        principalTable: "transport_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "transport_order_lines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransportOrderId = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    Requested = table.Column<long>(type: "bigint", nullable: false),
                    Loaded = table.Column<long>(type: "bigint", nullable: false),
                    Delivered = table.Column<long>(type: "bigint", nullable: false),
                    Returned = table.Column<long>(type: "bigint", nullable: false),
                    Lost = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transport_order_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transport_order_lines_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transport_order_lines_transport_orders_TransportOrderId",
                        column: x => x.TransportOrderId,
                        principalTable: "transport_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "transport_stops",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransportOrderId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    StorageId = table.Column<int>(type: "int", nullable: false),
                    StructureId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReroutedFromStopId = table.Column<int>(type: "int", nullable: true),
                    DistanceBlocks = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    DistanceSource = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EtaAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ArrivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transport_stops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transport_stops_storages_StorageId",
                        column: x => x.StorageId,
                        principalTable: "storages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transport_stops_structures_StructureId",
                        column: x => x.StructureId,
                        principalTable: "structures",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_transport_stops_transport_orders_TransportOrderId",
                        column: x => x.TransportOrderId,
                        principalTable: "transport_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "transport_stop_lines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransportStopId = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    Planned = table.Column<long>(type: "bigint", nullable: false),
                    Delivered = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transport_stop_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transport_stop_lines_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_transport_stop_lines_transport_stops_TransportStopId",
                        column: x => x.TransportStopId,
                        principalTable: "transport_stops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_transport_assignments_CarrierStructureId_ReleasedAt",
                table: "transport_assignments",
                columns: new[] { "CarrierStructureId", "ReleasedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_transport_assignments_TransportOrderId",
                table: "transport_assignments",
                column: "TransportOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_events_TransportOrderId_At",
                table: "transport_events",
                columns: new[] { "TransportOrderId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_transport_lanes_FromStructureId_ToStructureId",
                table: "transport_lanes",
                columns: new[] { "FromStructureId", "ToStructureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transport_lanes_ToStructureId",
                table: "transport_lanes",
                column: "ToStructureId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_order_lines_ItemBlueprintId",
                table: "transport_order_lines",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_order_lines_TransportOrderId_ItemBlueprintId",
                table: "transport_order_lines",
                columns: new[] { "TransportOrderId", "ItemBlueprintId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_CargoStorageId",
                table: "transport_orders",
                column: "CargoStorageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_CarrierStructureId",
                table: "transport_orders",
                column: "CarrierStructureId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_ClientKey",
                table: "transport_orders",
                column: "ClientKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_NextActionAt",
                table: "transport_orders",
                column: "NextActionAt");

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_SourceStorageId",
                table: "transport_orders",
                column: "SourceStorageId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_SourceStructureId_Origin_Status",
                table: "transport_orders",
                columns: new[] { "SourceStructureId", "Origin", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_transport_orders_Status_Origin",
                table: "transport_orders",
                columns: new[] { "Status", "Origin" });

            migrationBuilder.CreateIndex(
                name: "IX_transport_stop_lines_ItemBlueprintId",
                table: "transport_stop_lines",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_stop_lines_TransportStopId_ItemBlueprintId",
                table: "transport_stop_lines",
                columns: new[] { "TransportStopId", "ItemBlueprintId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transport_stops_StorageId",
                table: "transport_stops",
                column: "StorageId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_stops_StructureId",
                table: "transport_stops",
                column: "StructureId");

            migrationBuilder.CreateIndex(
                name: "IX_transport_stops_TransportOrderId_Sequence",
                table: "transport_stops",
                columns: new[] { "TransportOrderId", "Sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transport_assignments");

            migrationBuilder.DropTable(
                name: "transport_events");

            migrationBuilder.DropTable(
                name: "transport_lanes");

            migrationBuilder.DropTable(
                name: "transport_order_lines");

            migrationBuilder.DropTable(
                name: "transport_stop_lines");

            migrationBuilder.DropTable(
                name: "transport_stops");

            migrationBuilder.DropTable(
                name: "transport_orders");
        }
    }
}
