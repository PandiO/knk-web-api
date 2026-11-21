using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddShops : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_currency_entries_Account",
                table: "currency_entries");

            migrationBuilder.AddColumn<int>(
                name: "TownId",
                table: "currency_entries",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "shop_types",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DefaultKind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shop_types", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "town_accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TownId = table.Column<int>(type: "int", nullable: false),
                    Coins = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_town_accounts", x => x.Id);
                    table.CheckConstraint("CK_town_accounts_Coins", "`Coins` >= 0");
                    table.ForeignKey(
                        name: "FK_town_accounts_towns_TownId",
                        column: x => x.TownId,
                        principalTable: "towns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "shop_type_categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ShopTypeId = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shop_type_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shop_type_categories_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_shop_type_categories_shop_types_ShopTypeId",
                        column: x => x.ShopTypeId,
                        principalTable: "shop_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "shop_type_levels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ShopTypeId = table.Column<int>(type: "int", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    CycleSpeedMultiplier = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    YieldMultiplier = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    InputCostMultiplier = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    PriceMultiplier = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    MerchandiseMaxUnits = table.Column<long>(type: "bigint", nullable: true),
                    InputMaxUnits = table.Column<long>(type: "bigint", nullable: true),
                    UnitsPerSlot = table.Column<int>(type: "int", nullable: true),
                    ReorderPointUnits = table.Column<long>(type: "bigint", nullable: true),
                    TargetUnits = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shop_type_levels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shop_type_levels_shop_types_ShopTypeId",
                        column: x => x.ShopTypeId,
                        principalTable: "shop_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "shops",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    ShopTypeId = table.Column<int>(type: "int", nullable: true),
                    Kind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TaxModifier = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    BuyBackPercent = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    ShopkeeperLocationId = table.Column<int>(type: "int", nullable: true),
                    HideSoldOut = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shops_locations_ShopkeeperLocationId",
                        column: x => x.ShopkeeperLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_shops_production_structures_Id",
                        column: x => x.Id,
                        principalTable: "production_structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_shops_shop_types_ShopTypeId",
                        column: x => x.ShopTypeId,
                        principalTable: "shop_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "shop_catalogue_entries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ShopId = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    ItemInstanceId = table.Column<long>(type: "bigint", nullable: true),
                    StaticPrice = table.Column<long>(type: "bigint", nullable: true),
                    StockMode = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Pinned = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shop_catalogue_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shop_catalogue_entries_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shop_catalogue_entries_item_instances_ItemInstanceId",
                        column: x => x.ItemInstanceId,
                        principalTable: "item_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shop_catalogue_entries_shops_ShopId",
                        column: x => x.ShopId,
                        principalTable: "shops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "shop_trade_stats",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ShopId = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    HourStart = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UnitsSold = table.Column<long>(type: "bigint", nullable: false),
                    Revenue = table.Column<long>(type: "bigint", nullable: false),
                    UnitsBoughtBack = table.Column<long>(type: "bigint", nullable: false),
                    BuyBackPaid = table.Column<long>(type: "bigint", nullable: false),
                    UnitsSupplied = table.Column<long>(type: "bigint", nullable: false),
                    DistinctCustomers = table.Column<int>(type: "int", nullable: false),
                    TownPlayerCount = table.Column<int>(type: "int", nullable: true),
                    AvgSellPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    StockAtEnd = table.Column<long>(type: "bigint", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shop_trade_stats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shop_trade_stats_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_shop_trade_stats_shops_ShopId",
                        column: x => x.ShopId,
                        principalTable: "shops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_currency_entries_TownId_Currency_Id",
                table: "currency_entries",
                columns: new[] { "TownId", "Currency", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_currency_entries_Account",
                table: "currency_entries",
                sql: "(`AccountKind` = 0 AND `UserId` IS NOT NULL AND `SystemAccount` IS NULL AND `TownId` IS NULL AND `BalanceBefore` IS NOT NULL AND `BalanceAfter` IS NOT NULL) OR (`AccountKind` = 1 AND `UserId` IS NULL AND `SystemAccount` IS NOT NULL AND `TownId` IS NULL AND `BalanceBefore` IS NULL AND `BalanceAfter` IS NULL) OR (`AccountKind` = 2 AND `UserId` IS NULL AND `SystemAccount` IS NULL AND `TownId` IS NOT NULL AND `BalanceBefore` IS NOT NULL AND `BalanceAfter` IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_shop_catalogue_entries_ItemBlueprintId",
                table: "shop_catalogue_entries",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_shop_catalogue_entries_ItemInstanceId",
                table: "shop_catalogue_entries",
                column: "ItemInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_shop_catalogue_entries_ShopId_ItemBlueprintId",
                table: "shop_catalogue_entries",
                columns: new[] { "ShopId", "ItemBlueprintId" });

            migrationBuilder.CreateIndex(
                name: "IX_shop_trade_stats_HourStart",
                table: "shop_trade_stats",
                column: "HourStart");

            migrationBuilder.CreateIndex(
                name: "IX_shop_trade_stats_ItemBlueprintId",
                table: "shop_trade_stats",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_shop_trade_stats_ShopId_ItemBlueprintId_HourStart",
                table: "shop_trade_stats",
                columns: new[] { "ShopId", "ItemBlueprintId", "HourStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shop_type_categories_CategoryId",
                table: "shop_type_categories",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_shop_type_categories_ShopTypeId_CategoryId",
                table: "shop_type_categories",
                columns: new[] { "ShopTypeId", "CategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shop_type_levels_ShopTypeId_Level",
                table: "shop_type_levels",
                columns: new[] { "ShopTypeId", "Level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shop_types_Name",
                table: "shop_types",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shops_ShopkeeperLocationId",
                table: "shops",
                column: "ShopkeeperLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_shops_ShopTypeId",
                table: "shops",
                column: "ShopTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_town_accounts_TownId",
                table: "town_accounts",
                column: "TownId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shop_catalogue_entries");

            migrationBuilder.DropTable(
                name: "shop_trade_stats");

            migrationBuilder.DropTable(
                name: "shop_type_categories");

            migrationBuilder.DropTable(
                name: "shop_type_levels");

            migrationBuilder.DropTable(
                name: "town_accounts");

            migrationBuilder.DropTable(
                name: "shops");

            migrationBuilder.DropTable(
                name: "shop_types");

            migrationBuilder.DropIndex(
                name: "IX_currency_entries_TownId_Currency_Id",
                table: "currency_entries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_currency_entries_Account",
                table: "currency_entries");

            migrationBuilder.DropColumn(
                name: "TownId",
                table: "currency_entries");

            migrationBuilder.AddCheckConstraint(
                name: "CK_currency_entries_Account",
                table: "currency_entries",
                sql: "(`AccountKind` = 0 AND `UserId` IS NOT NULL AND `SystemAccount` IS NULL AND `BalanceBefore` IS NOT NULL AND `BalanceAfter` IS NOT NULL) OR (`AccountKind` = 1 AND `UserId` IS NULL AND `SystemAccount` IS NOT NULL AND `BalanceBefore` IS NULL AND `BalanceAfter` IS NULL)");
        }
    }
}
