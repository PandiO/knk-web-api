using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddSiegeShopCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PotionType",
                table: "item_blueprints",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "siege_shop_categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Key = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IconMaterial = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_shop_entries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GameMode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SiegeScenarioId = table.Column<int>(type: "int", nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    SiegeWeaponTemplateId = table.Column<int>(type: "int", nullable: true),
                    DisplayName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IconMaterial = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Price = table.Column<int>(type: "int", nullable: true),
                    PriceStep = table.Column<int>(type: "int", nullable: true),
                    PriceCurve = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PriceGrowthFactor = table.Column<double>(type: "double", nullable: true),
                    PriceMax = table.Column<int>(type: "int", nullable: true),
                    CooldownSeconds = table.Column<int>(type: "int", nullable: true),
                    CooldownStepSeconds = table.Column<int>(type: "int", nullable: true),
                    CooldownCurve = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CooldownGrowthFactor = table.Column<double>(type: "double", nullable: true),
                    CooldownMaxSeconds = table.Column<int>(type: "int", nullable: true),
                    MaxPerMatch = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_shop_entries_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_shop_entries_siege_scenarios_SiegeScenarioId",
                        column: x => x.SiegeScenarioId,
                        principalTable: "siege_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_siege_shop_entries_siege_shop_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "siege_shop_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_siege_shop_entries_siege_weapon_templates_SiegeWeaponTemplat~",
                        column: x => x.SiegeWeaponTemplateId,
                        principalTable: "siege_weapon_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_scenario_shop_overrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SiegeScenarioId = table.Column<int>(type: "int", nullable: false),
                    SiegeShopEntryId = table.Column<int>(type: "int", nullable: false),
                    Hidden = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Price = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: true),
                    MaxPerMatch = table.Column<int>(type: "int", nullable: true),
                    CooldownSeconds = table.Column<int>(type: "int", nullable: true),
                    PriceStep = table.Column<int>(type: "int", nullable: true),
                    CooldownStepSeconds = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_scenario_shop_overrides_siege_scenarios_SiegeScenarioId",
                        column: x => x.SiegeScenarioId,
                        principalTable: "siege_scenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_siege_scenario_shop_overrides_siege_shop_entries_SiegeShopEn~",
                        column: x => x.SiegeShopEntryId,
                        principalTable: "siege_shop_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.InsertData(
                table: "siege_shop_categories",
                columns: new[] { "Id", "IconMaterial", "Key", "Name", "SortOrder" },
                values: new object[,]
                {
                    { 1, "minecraft:crossbow", "siege-weapons", "Siege weapons", 0 },
                    { 2, "minecraft:golden_apple", "consumables", "Consumables", 1 },
                    { 3, "minecraft:blaze_rod", "magic", "Magic", 2 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_siege_scenario_shop_overrides_SiegeScenarioId_SiegeShopEntry~",
                table: "siege_scenario_shop_overrides",
                columns: new[] { "SiegeScenarioId", "SiegeShopEntryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_siege_scenario_shop_overrides_SiegeShopEntryId",
                table: "siege_scenario_shop_overrides",
                column: "SiegeShopEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_shop_categories_Key",
                table: "siege_shop_categories",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_siege_shop_entries_CategoryId",
                table: "siege_shop_entries",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_shop_entries_GameMode_SiegeScenarioId_SortOrder",
                table: "siege_shop_entries",
                columns: new[] { "GameMode", "SiegeScenarioId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_siege_shop_entries_ItemBlueprintId",
                table: "siege_shop_entries",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_shop_entries_SiegeScenarioId",
                table: "siege_shop_entries",
                column: "SiegeScenarioId");

            migrationBuilder.CreateIndex(
                name: "IX_siege_shop_entries_SiegeWeaponTemplateId",
                table: "siege_shop_entries",
                column: "SiegeWeaponTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "siege_scenario_shop_overrides");

            migrationBuilder.DropTable(
                name: "siege_shop_entries");

            migrationBuilder.DropTable(
                name: "siege_shop_categories");

            migrationBuilder.DropColumn(
                name: "PotionType",
                table: "item_blueprints");
        }
    }
}
