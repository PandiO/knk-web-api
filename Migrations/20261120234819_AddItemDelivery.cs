using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddItemDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "item_deliveries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceType = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceRef = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActorKind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestHash = table.Column<string>(type: "char(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InboxStorageId = table.Column<int>(type: "int", nullable: false),
                    StockTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    OverflowTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    CapUnits = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_item_deliveries_storages_InboxStorageId",
                        column: x => x.InboxStorageId,
                        principalTable: "storages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_item_deliveries_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "item_delivery_lines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ItemDeliveryId = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    ItemInstanceId = table.Column<long>(type: "bigint", nullable: true),
                    Requested = table.Column<long>(type: "bigint", nullable: false),
                    Inboxed = table.Column<long>(type: "bigint", nullable: false),
                    Lost = table.Column<long>(type: "bigint", nullable: false),
                    Claimed = table.Column<long>(type: "bigint", nullable: false),
                    Expired = table.Column<long>(type: "bigint", nullable: false),
                    Remaining = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RemindersSent = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_delivery_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_item_delivery_lines_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_item_delivery_lines_item_deliveries_ItemDeliveryId",
                        column: x => x.ItemDeliveryId,
                        principalTable: "item_deliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_item_delivery_lines_item_instances_ItemInstanceId",
                        column: x => x.ItemInstanceId,
                        principalTable: "item_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_item_deliveries_IdempotencyKey",
                table: "item_deliveries",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_item_deliveries_InboxStorageId",
                table: "item_deliveries",
                column: "InboxStorageId");

            migrationBuilder.CreateIndex(
                name: "IX_item_deliveries_SourceType_SourceRef",
                table: "item_deliveries",
                columns: new[] { "SourceType", "SourceRef" });

            migrationBuilder.CreateIndex(
                name: "IX_item_deliveries_UserId_CreatedAt",
                table: "item_deliveries",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_item_delivery_lines_ItemBlueprintId",
                table: "item_delivery_lines",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_item_delivery_lines_ItemDeliveryId_LineNo",
                table: "item_delivery_lines",
                columns: new[] { "ItemDeliveryId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_item_delivery_lines_ItemInstanceId",
                table: "item_delivery_lines",
                column: "ItemInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_item_delivery_lines_Remaining_ExpiresAt",
                table: "item_delivery_lines",
                columns: new[] { "Remaining", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_delivery_lines");

            migrationBuilder.DropTable(
                name: "item_deliveries");
        }
    }
}
