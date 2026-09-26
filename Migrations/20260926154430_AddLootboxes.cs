using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddLootboxes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lootbox_configurations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    GlobalMaxActive = table.Column<int>(type: "int", nullable: false),
                    MaxClaimsPerPlayerPerDay = table.Column<int>(type: "int", nullable: true),
                    AnnounceMinItemStars = table.Column<int>(type: "int", nullable: false),
                    AnnounceSpawnMinBoxStars = table.Column<int>(type: "int", nullable: false),
                    DropAnnouncementTemplate = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SpawnAnnouncementTemplate = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lootbox_configurations", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "lootbox_spawn_areas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    World = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WgRegionId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MaxActive = table.Column<int>(type: "int", nullable: false),
                    SpawnIntervalSeconds = table.Column<int>(type: "int", nullable: false),
                    SpawnChancePercent = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    MinOnlinePlayers = table.Column<int>(type: "int", nullable: false),
                    MinDistanceFromPlayers = table.Column<int>(type: "int", nullable: false),
                    LifetimeMinutes = table.Column<int>(type: "int", nullable: false),
                    ExcludedRegionIds = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lootbox_spawn_areas_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "lootbox_types",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    IncludeSubcategories = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SpawnWeight = table.Column<int>(type: "int", nullable: false),
                    MinBoxStars = table.Column<int>(type: "int", nullable: false),
                    MaxBoxStars = table.Column<int>(type: "int", nullable: false),
                    ItemStarSpread = table.Column<int>(type: "int", nullable: false),
                    DisplayMaterialRefId = table.Column<int>(type: "int", nullable: true),
                    MaxClaimsPerPlayerPerDay = table.Column<int>(type: "int", nullable: true),
                    AnnounceMinItemStars = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lootbox_types_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lootbox_types_minecraftmaterialrefs_DisplayMaterialRefId",
                        column: x => x.DisplayMaterialRefId,
                        principalTable: "minecraftmaterialrefs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "lootbox_enchant_rolls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    LootboxTypeId = table.Column<int>(type: "int", nullable: false),
                    EnchantmentDefinitionId = table.Column<int>(type: "int", nullable: false),
                    ChancePercent = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    MinLevel = table.Column<int>(type: "int", nullable: false),
                    MaxLevel = table.Column<int>(type: "int", nullable: false),
                    MinBoxStars = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lootbox_enchant_rolls_EnchantmentDefinitions_EnchantmentDefi~",
                        column: x => x.EnchantmentDefinitionId,
                        principalTable: "EnchantmentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lootbox_enchant_rolls_lootbox_types_LootboxTypeId",
                        column: x => x.LootboxTypeId,
                        principalTable: "lootbox_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "lootbox_pool_entries",
                columns: table => new
                {
                    LootboxTypeId = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    Mode = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WeightOverride = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    GradeIdOverride = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lootbox_pool_entries", x => new { x.LootboxTypeId, x.ItemBlueprintId });
                    table.ForeignKey(
                        name: "FK_lootbox_pool_entries_grades_GradeIdOverride",
                        column: x => x.GradeIdOverride,
                        principalTable: "grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lootbox_pool_entries_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lootbox_pool_entries_lootbox_types_LootboxTypeId",
                        column: x => x.LootboxTypeId,
                        principalTable: "lootbox_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "lootbox_spawn_area_types",
                columns: table => new
                {
                    LootboxSpawnAreaId = table.Column<int>(type: "int", nullable: false),
                    LootboxTypeId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lootbox_spawn_area_types", x => new { x.LootboxSpawnAreaId, x.LootboxTypeId });
                    table.ForeignKey(
                        name: "FK_lootbox_spawn_area_types_lootbox_spawn_areas_LootboxSpawnAre~",
                        column: x => x.LootboxSpawnAreaId,
                        principalTable: "lootbox_spawn_areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_lootbox_spawn_area_types_lootbox_types_LootboxTypeId",
                        column: x => x.LootboxTypeId,
                        principalTable: "lootbox_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "lootbox_spawns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Token = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    LootboxTypeId = table.Column<int>(type: "int", nullable: false),
                    BoxGradeId = table.Column<int>(type: "int", nullable: false),
                    SpawnAreaId = table.Column<int>(type: "int", nullable: true),
                    World = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    X = table.Column<int>(type: "int", nullable: false),
                    Y = table.Column<int>(type: "int", nullable: false),
                    Z = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SpawnedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    ClaimedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    ClaimedByUserId = table.Column<int>(type: "int", nullable: true),
                    ServerId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lootbox_spawns_grades_BoxGradeId",
                        column: x => x.BoxGradeId,
                        principalTable: "grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lootbox_spawns_lootbox_spawn_areas_SpawnAreaId",
                        column: x => x.SpawnAreaId,
                        principalTable: "lootbox_spawn_areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_lootbox_spawns_lootbox_types_LootboxTypeId",
                        column: x => x.LootboxTypeId,
                        principalTable: "lootbox_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lootbox_spawns_users_ClaimedByUserId",
                        column: x => x.ClaimedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_lootbox_spawns_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "lootbox_special_entries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    LootboxTypeId = table.Column<int>(type: "int", nullable: true),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    ChancePerMillion = table.Column<int>(type: "int", nullable: false),
                    MinBoxStars = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lootbox_special_entries_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lootbox_special_entries_lootbox_types_LootboxTypeId",
                        column: x => x.LootboxTypeId,
                        principalTable: "lootbox_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "lootbox_type_grade_weights",
                columns: table => new
                {
                    LootboxTypeId = table.Column<int>(type: "int", nullable: false),
                    GradeId = table.Column<int>(type: "int", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lootbox_type_grade_weights", x => new { x.LootboxTypeId, x.GradeId });
                    table.ForeignKey(
                        name: "FK_lootbox_type_grade_weights_grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lootbox_type_grade_weights_lootbox_types_LootboxTypeId",
                        column: x => x.LootboxTypeId,
                        principalTable: "lootbox_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "lootbox_claims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    LootboxSpawnId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    LootboxTypeId = table.Column<int>(type: "int", nullable: false),
                    BoxGradeId = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    ItemGradeId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    IsSpecial = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ItemInstanceId = table.Column<long>(type: "bigint", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClaimedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    DeliveredAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    DeliveryMethod = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeliveryNote = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lootbox_claims_grades_BoxGradeId",
                        column: x => x.BoxGradeId,
                        principalTable: "grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lootbox_claims_grades_ItemGradeId",
                        column: x => x.ItemGradeId,
                        principalTable: "grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lootbox_claims_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lootbox_claims_item_instances_ItemInstanceId",
                        column: x => x.ItemInstanceId,
                        principalTable: "item_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lootbox_claims_lootbox_spawns_LootboxSpawnId",
                        column: x => x.LootboxSpawnId,
                        principalTable: "lootbox_spawns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lootbox_claims_lootbox_types_LootboxTypeId",
                        column: x => x.LootboxTypeId,
                        principalTable: "lootbox_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lootbox_claims_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_claims_BoxGradeId",
                table: "lootbox_claims",
                column: "BoxGradeId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_claims_IdempotencyKey",
                table: "lootbox_claims",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_claims_ItemBlueprintId",
                table: "lootbox_claims",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_claims_ItemGradeId",
                table: "lootbox_claims",
                column: "ItemGradeId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_claims_ItemInstanceId",
                table: "lootbox_claims",
                column: "ItemInstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_claims_LootboxSpawnId",
                table: "lootbox_claims",
                column: "LootboxSpawnId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_claims_LootboxTypeId",
                table: "lootbox_claims",
                column: "LootboxTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_claims_UserId_ClaimedAt",
                table: "lootbox_claims",
                columns: new[] { "UserId", "ClaimedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_enchant_rolls_EnchantmentDefinitionId",
                table: "lootbox_enchant_rolls",
                column: "EnchantmentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_enchant_rolls_LootboxTypeId_SortOrder",
                table: "lootbox_enchant_rolls",
                columns: new[] { "LootboxTypeId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_pool_entries_GradeIdOverride",
                table: "lootbox_pool_entries",
                column: "GradeIdOverride");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_pool_entries_ItemBlueprintId",
                table: "lootbox_pool_entries",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_spawn_area_types_LootboxTypeId",
                table: "lootbox_spawn_area_types",
                column: "LootboxTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_spawn_areas_CreatedByUserId",
                table: "lootbox_spawn_areas",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_spawn_areas_Name",
                table: "lootbox_spawn_areas",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_spawns_BoxGradeId",
                table: "lootbox_spawns",
                column: "BoxGradeId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_spawns_ClaimedByUserId",
                table: "lootbox_spawns",
                column: "ClaimedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_spawns_CreatedByUserId",
                table: "lootbox_spawns",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_spawns_LootboxTypeId",
                table: "lootbox_spawns",
                column: "LootboxTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_spawns_SpawnAreaId_Status",
                table: "lootbox_spawns",
                columns: new[] { "SpawnAreaId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_spawns_Status_ExpiresAt",
                table: "lootbox_spawns",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_spawns_Token",
                table: "lootbox_spawns",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_special_entries_ItemBlueprintId",
                table: "lootbox_special_entries",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_special_entries_LootboxTypeId",
                table: "lootbox_special_entries",
                column: "LootboxTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_type_grade_weights_GradeId",
                table: "lootbox_type_grade_weights",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_types_CategoryId",
                table: "lootbox_types",
                column: "CategoryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_types_DisplayMaterialRefId",
                table: "lootbox_types",
                column: "DisplayMaterialRefId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lootbox_claims");

            migrationBuilder.DropTable(
                name: "lootbox_configurations");

            migrationBuilder.DropTable(
                name: "lootbox_enchant_rolls");

            migrationBuilder.DropTable(
                name: "lootbox_pool_entries");

            migrationBuilder.DropTable(
                name: "lootbox_spawn_area_types");

            migrationBuilder.DropTable(
                name: "lootbox_special_entries");

            migrationBuilder.DropTable(
                name: "lootbox_type_grade_weights");

            migrationBuilder.DropTable(
                name: "lootbox_spawns");

            migrationBuilder.DropTable(
                name: "lootbox_spawn_areas");

            migrationBuilder.DropTable(
                name: "lootbox_types");
        }
    }
}
