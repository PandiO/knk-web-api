using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionYieldAreas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "YieldSource",
                table: "production_structures",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Configured",
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "PrimaryMultiplier",
                table: "production_kinds",
                type: "decimal(12,4)",
                precision: 12,
                scale: 4,
                nullable: false,
                defaultValue: 1.5m);

            migrationBuilder.AddColumn<string>(
                name: "SurveyProfileJson",
                table: "production_kinds",
                type: "varchar(4000)",
                maxLength: 4000,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "SurveyProfileVersion",
                table: "production_kinds",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "production_areas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProductionStructureId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Model = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Shape = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Exclusive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    WorldName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WgRegionId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PointsJson = table.Column<string>(type: "varchar(8000)", maxLength: 8000, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CenterX = table.Column<int>(type: "int", nullable: true),
                    CenterY = table.Column<int>(type: "int", nullable: true),
                    CenterZ = table.Column<int>(type: "int", nullable: true),
                    Radius = table.Column<int>(type: "int", nullable: true),
                    MinX = table.Column<int>(type: "int", nullable: false),
                    MinY = table.Column<int>(type: "int", nullable: false),
                    MinZ = table.Column<int>(type: "int", nullable: false),
                    MaxX = table.Column<int>(type: "int", nullable: false),
                    MaxY = table.Column<int>(type: "int", nullable: false),
                    MaxZ = table.Column<int>(type: "int", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SurveyRequestedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SurveyRequestTrigger = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastSurveyId = table.Column<long>(type: "bigint", nullable: true),
                    LastSurveyedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_areas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_production_areas_production_structures_ProductionStructureId",
                        column: x => x.ProductionStructureId,
                        principalTable: "production_structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "production_kind_primary_outputs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProductionKindId = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_kind_primary_outputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_production_kind_primary_outputs_item_blueprints_ItemBlueprin~",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_kind_primary_outputs_production_kinds_ProductionK~",
                        column: x => x.ProductionKindId,
                        principalTable: "production_kinds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "production_yield_overrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProductionStructureId = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_yield_overrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_production_yield_overrides_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_yield_overrides_production_structures_ProductionS~",
                        column: x => x.ProductionStructureId,
                        principalTable: "production_structures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "production_yield_rows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProductionKindId = table.Column<int>(type: "int", nullable: false),
                    UnitKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    RatePerUnitPerHour = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ByproductChance = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    MinLevel = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_yield_rows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_production_yield_rows_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_production_yield_rows_production_kinds_ProductionKindId",
                        column: x => x.ProductionKindId,
                        principalTable: "production_kinds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "area_surveys",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProductionAreaId = table.Column<int>(type: "int", nullable: false),
                    ProductionStructureId = table.Column<int>(type: "int", nullable: false),
                    SurveyedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ProfileVersion = table.Column<int>(type: "int", nullable: false),
                    Trigger = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContentHash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TotalUnits = table.Column<long>(type: "bigint", nullable: false),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_area_surveys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_area_surveys_production_areas_ProductionAreaId",
                        column: x => x.ProductionAreaId,
                        principalTable: "production_areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "area_survey_units",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AreaSurveyId = table.Column<long>(type: "bigint", nullable: false),
                    UnitKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Count = table.Column<long>(type: "bigint", nullable: false),
                    SharedBy = table.Column<int>(type: "int", nullable: false),
                    Extra = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_area_survey_units", x => x.Id);
                    table.ForeignKey(
                        name: "FK_area_survey_units_area_surveys_AreaSurveyId",
                        column: x => x.AreaSurveyId,
                        principalTable: "area_surveys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.UpdateData(
                table: "production_kinds",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "PrimaryMultiplier", "SurveyProfileJson", "SurveyProfileVersion" },
                values: new object[] { 1.5m, "{\"unitTypes\":[\"CROP\"]}", 1 });

            migrationBuilder.UpdateData(
                table: "production_kinds",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "PrimaryMultiplier", "SurveyProfileJson", "SurveyProfileVersion" },
                values: new object[] { 1.5m, "{\"unitTypes\":[\"TREE\",\"SAPLING\"]}", 1 });

            migrationBuilder.UpdateData(
                table: "production_kinds",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "PrimaryMultiplier", "SurveyProfileJson", "SurveyProfileVersion" },
                values: new object[] { 1.5m, "{\"unitTypes\":[\"BLOCK\"]}", 1 });

            migrationBuilder.UpdateData(
                table: "production_kinds",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "PrimaryMultiplier", "SurveyProfileJson", "SurveyProfileVersion" },
                values: new object[] { 1.5m, "{\"unitTypes\":[\"BLOCK\"]}", 1 });

            migrationBuilder.UpdateData(
                table: "production_kinds",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "PrimaryMultiplier", "SurveyProfileJson", "SurveyProfileVersion" },
                values: new object[] { 1.5m, "{\"unitTypes\":[\"WATER\"]}", 1 });

            migrationBuilder.UpdateData(
                table: "production_kinds",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "PrimaryMultiplier", "SurveyProfileJson", "SurveyProfileVersion" },
                values: new object[] { 1.5m, "{\"unitTypes\":[\"FLOWER\"]}", 1 });

            migrationBuilder.UpdateData(
                table: "production_kinds",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "PrimaryMultiplier", "SurveyProfileJson", "SurveyProfileVersion" },
                values: new object[] { 1.5m, "{\"unitTypes\":[\"FLOWER\"]}", 1 });

            migrationBuilder.UpdateData(
                table: "production_kinds",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "PrimaryMultiplier", "SurveyProfileJson", "SurveyProfileVersion" },
                values: new object[] { 1.5m, "{\"unitTypes\":[\"ENTITY\"]}", 1 });

            migrationBuilder.CreateIndex(
                name: "IX_area_survey_units_AreaSurveyId_UnitKey_SharedBy",
                table: "area_survey_units",
                columns: new[] { "AreaSurveyId", "UnitKey", "SharedBy" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_area_surveys_ProductionAreaId_SurveyedAt",
                table: "area_surveys",
                columns: new[] { "ProductionAreaId", "SurveyedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_area_surveys_ProductionStructureId_SurveyedAt",
                table: "area_surveys",
                columns: new[] { "ProductionStructureId", "SurveyedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_production_areas_ProductionStructureId_Name",
                table: "production_areas",
                columns: new[] { "ProductionStructureId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_production_areas_WorldName_Model_Exclusive",
                table: "production_areas",
                columns: new[] { "WorldName", "Model", "Exclusive" });

            migrationBuilder.CreateIndex(
                name: "IX_production_kind_primary_outputs_ItemBlueprintId",
                table: "production_kind_primary_outputs",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_production_kind_primary_outputs_ProductionKindId_ItemBluepri~",
                table: "production_kind_primary_outputs",
                columns: new[] { "ProductionKindId", "ItemBlueprintId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_production_yield_overrides_ItemBlueprintId",
                table: "production_yield_overrides",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_production_yield_overrides_ProductionStructureId_ItemBluepri~",
                table: "production_yield_overrides",
                columns: new[] { "ProductionStructureId", "ItemBlueprintId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_production_yield_rows_ItemBlueprintId",
                table: "production_yield_rows",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_production_yield_rows_ProductionKindId_UnitKey_ItemBlueprint~",
                table: "production_yield_rows",
                columns: new[] { "ProductionKindId", "UnitKey", "ItemBlueprintId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "area_survey_units");

            migrationBuilder.DropTable(
                name: "production_kind_primary_outputs");

            migrationBuilder.DropTable(
                name: "production_yield_overrides");

            migrationBuilder.DropTable(
                name: "production_yield_rows");

            migrationBuilder.DropTable(
                name: "area_surveys");

            migrationBuilder.DropTable(
                name: "production_areas");

            migrationBuilder.DropColumn(
                name: "YieldSource",
                table: "production_structures");

            migrationBuilder.DropColumn(
                name: "PrimaryMultiplier",
                table: "production_kinds");

            migrationBuilder.DropColumn(
                name: "SurveyProfileJson",
                table: "production_kinds");

            migrationBuilder.DropColumn(
                name: "SurveyProfileVersion",
                table: "production_kinds");
        }
    }
}
