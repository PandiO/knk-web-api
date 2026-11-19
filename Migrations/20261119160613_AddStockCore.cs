using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddStockCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "StorageWeight",
                table: "item_blueprints",
                type: "decimal(12,4)",
                precision: 12,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.CreateTable(
                name: "stock_transactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Reason = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Mode = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActorKind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    ActorComponent = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceRef = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IdempotencyKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestHash = table.Column<string>(type: "char(64)", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReversesTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_transactions_stock_transactions_ReversesTransactionId",
                        column: x => x.ReversesTransactionId,
                        principalTable: "stock_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "storages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Kind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Purpose = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DomainId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    ClanId = table.Column<int>(type: "int", nullable: true),
                    SystemKey = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MaxUnits = table.Column<long>(type: "bigint", nullable: true),
                    MaxDistinct = table.Column<int>(type: "int", nullable: true),
                    UnitsPerSlot = table.Column<int>(type: "int", nullable: true),
                    TotalUnits = table.Column<long>(type: "bigint", nullable: false),
                    DistinctItems = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LockReason = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.CheckConstraint("CK_storages_Capacity", "(`MaxUnits` IS NULL OR `MaxUnits` >= 0) AND (`MaxDistinct` IS NULL OR `MaxDistinct` >= 0) AND (`UnitsPerSlot` IS NULL OR `UnitsPerSlot` >= 0)");
                    table.CheckConstraint("CK_storages_Holder", "(`Kind` = 'Domain' AND `DomainId` IS NOT NULL AND `UserId` IS NULL AND `ClanId` IS NULL AND `SystemKey` IS NULL) OR (`Kind` = 'Player' AND `UserId` IS NOT NULL AND `DomainId` IS NULL AND `ClanId` IS NULL AND `SystemKey` IS NULL) OR (`Kind` = 'Clan' AND `ClanId` IS NOT NULL AND `DomainId` IS NULL AND `UserId` IS NULL AND `SystemKey` IS NULL) OR (`Kind` = 'Transit' AND `DomainId` IS NULL AND `UserId` IS NULL AND `ClanId` IS NULL AND `SystemKey` IS NULL) OR (`Kind` = 'Virtual' AND `SystemKey` IS NOT NULL AND `DomainId` IS NULL AND `UserId` IS NULL AND `ClanId` IS NULL)");
                    table.ForeignKey(
                        name: "FK_storages_clans_ClanId",
                        column: x => x.ClanId,
                        principalTable: "clans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_storages_domains_DomainId",
                        column: x => x.DomainId,
                        principalTable: "domains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_storages_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "stock_lines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    StorageId = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_lines_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_lines_storages_StorageId",
                        column: x => x.StorageId,
                        principalTable: "storages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "stock_move_rollups",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Day = table.Column<DateTime>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FromStorageId = table.Column<int>(type: "int", nullable: false),
                    ToStorageId = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    MoveCount = table.Column<int>(type: "int", nullable: false),
                    RolledUpAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_move_rollups_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_move_rollups_storages_FromStorageId",
                        column: x => x.FromStorageId,
                        principalTable: "storages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_move_rollups_storages_ToStorageId",
                        column: x => x.ToStorageId,
                        principalTable: "storages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "stock_moves",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransactionId = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    FromStorageId = table.Column<int>(type: "int", nullable: false),
                    ToStorageId = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: true),
                    ItemInstanceId = table.Column<long>(type: "bigint", nullable: true),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.CheckConstraint("CK_stock_moves_Item", "(`ItemBlueprintId` IS NOT NULL AND `ItemInstanceId` IS NULL AND `Amount` > 0) OR (`ItemInstanceId` IS NOT NULL AND `ItemBlueprintId` IS NULL AND `Amount` = 1)");
                    table.CheckConstraint("CK_stock_moves_Storages", "`FromStorageId` <> `ToStorageId`");
                    table.ForeignKey(
                        name: "FK_stock_moves_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_moves_item_instances_ItemInstanceId",
                        column: x => x.ItemInstanceId,
                        principalTable: "item_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_moves_stock_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "stock_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_moves_storages_FromStorageId",
                        column: x => x.FromStorageId,
                        principalTable: "storages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_moves_storages_ToStorageId",
                        column: x => x.ToStorageId,
                        principalTable: "storages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "stock_units",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    StorageId = table.Column<int>(type: "int", nullable: false),
                    ItemInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stock_units_item_instances_ItemInstanceId",
                        column: x => x.ItemInstanceId,
                        principalTable: "item_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_units_storages_StorageId",
                        column: x => x.StorageId,
                        principalTable: "storages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.InsertData(
                table: "storages",
                columns: new[] { "Id", "ClanId", "CreatedAt", "DistinctItems", "DomainId", "Kind", "LockReason", "LockedUntil", "MaxDistinct", "MaxUnits", "Name", "Purpose", "RowVersion", "SystemKey", "TotalUnits", "UnitsPerSlot", "UserId" },
                values: new object[,]
                {
                    { 1, null, new DateTime(2026, 11, 19, 0, 0, 0, 0, DateTimeKind.Utc), 0, null, "Virtual", null, null, null, null, "SYS_PRODUCTION", "General", 0L, "SYS_PRODUCTION", 0L, null, null },
                    { 2, null, new DateTime(2026, 11, 19, 0, 0, 0, 0, DateTimeKind.Utc), 0, null, "Virtual", null, null, null, null, "SYS_CONSUMPTION", "General", 0L, "SYS_CONSUMPTION", 0L, null, null },
                    { 3, null, new DateTime(2026, 11, 19, 0, 0, 0, 0, DateTimeKind.Utc), 0, null, "Virtual", null, null, null, null, "SYS_LOSS", "General", 0L, "SYS_LOSS", 0L, null, null },
                    { 4, null, new DateTime(2026, 11, 19, 0, 0, 0, 0, DateTimeKind.Utc), 0, null, "Virtual", null, null, null, null, "SYS_ADJUSTMENT", "General", 0L, "SYS_ADJUSTMENT", 0L, null, null },
                    { 5, null, new DateTime(2026, 11, 19, 0, 0, 0, 0, DateTimeKind.Utc), 0, null, "Virtual", null, null, null, null, "SYS_WORLD", "General", 0L, "SYS_WORLD", 0L, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_stock_lines_ItemBlueprintId",
                table: "stock_lines",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_lines_StorageId_ItemBlueprintId",
                table: "stock_lines",
                columns: new[] { "StorageId", "ItemBlueprintId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_move_rollups_Day_Reason_FromStorageId_ToStorageId_Item~",
                table: "stock_move_rollups",
                columns: new[] { "Day", "Reason", "FromStorageId", "ToStorageId", "ItemBlueprintId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_move_rollups_FromStorageId_ItemBlueprintId",
                table: "stock_move_rollups",
                columns: new[] { "FromStorageId", "ItemBlueprintId" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_move_rollups_ItemBlueprintId",
                table: "stock_move_rollups",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_move_rollups_ToStorageId_ItemBlueprintId",
                table: "stock_move_rollups",
                columns: new[] { "ToStorageId", "ItemBlueprintId" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_moves_FromStorageId_ItemBlueprintId_CreatedAt",
                table: "stock_moves",
                columns: new[] { "FromStorageId", "ItemBlueprintId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_moves_ItemBlueprintId",
                table: "stock_moves",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_moves_ItemInstanceId_Id",
                table: "stock_moves",
                columns: new[] { "ItemInstanceId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_moves_Reason_CreatedAt",
                table: "stock_moves",
                columns: new[] { "Reason", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_moves_ToStorageId_ItemBlueprintId_CreatedAt",
                table: "stock_moves",
                columns: new[] { "ToStorageId", "ItemBlueprintId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_moves_TransactionId",
                table: "stock_moves",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transactions_ActorUserId_CreatedAt",
                table: "stock_transactions",
                columns: new[] { "ActorUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_transactions_CreatedAt",
                table: "stock_transactions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transactions_IdempotencyKey",
                table: "stock_transactions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_transactions_Reason_CreatedAt",
                table: "stock_transactions",
                columns: new[] { "Reason", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_transactions_ReversesTransactionId",
                table: "stock_transactions",
                column: "ReversesTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_transactions_SourceType_SourceRef",
                table: "stock_transactions",
                columns: new[] { "SourceType", "SourceRef" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_units_ItemInstanceId",
                table: "stock_units",
                column: "ItemInstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_units_StorageId",
                table: "stock_units",
                column: "StorageId");

            migrationBuilder.CreateIndex(
                name: "IX_storages_ClanId",
                table: "storages",
                column: "ClanId");

            migrationBuilder.CreateIndex(
                name: "IX_storages_DomainId",
                table: "storages",
                column: "DomainId");

            migrationBuilder.CreateIndex(
                name: "IX_storages_Kind_Purpose",
                table: "storages",
                columns: new[] { "Kind", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_storages_SystemKey",
                table: "storages",
                column: "SystemKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_storages_UserId",
                table: "storages",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_lines");

            migrationBuilder.DropTable(
                name: "stock_move_rollups");

            migrationBuilder.DropTable(
                name: "stock_moves");

            migrationBuilder.DropTable(
                name: "stock_units");

            migrationBuilder.DropTable(
                name: "stock_transactions");

            migrationBuilder.DropTable(
                name: "storages");

            migrationBuilder.DropColumn(
                name: "StorageWeight",
                table: "item_blueprints");
        }
    }
}
