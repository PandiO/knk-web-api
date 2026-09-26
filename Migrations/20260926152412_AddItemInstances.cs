using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddItemInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "item_instances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    GradeId = table.Column<int>(type: "int", nullable: true),
                    OwnerUserId = table.Column<int>(type: "int", nullable: true),
                    Origin = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OriginRef = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    OwnerCount = table.Column<int>(type: "int", nullable: false),
                    CustomDisplayName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsSoulbound = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsGhosted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_item_instances_grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_item_instances_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_item_instances_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "item_instance_enchantments",
                columns: table => new
                {
                    ItemInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    EnchantmentDefinitionId = table.Column<int>(type: "int", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_instance_enchantments", x => new { x.ItemInstanceId, x.EnchantmentDefinitionId });
                    table.ForeignKey(
                        name: "FK_item_instance_enchantments_EnchantmentDefinitions_Enchantmen~",
                        column: x => x.EnchantmentDefinitionId,
                        principalTable: "EnchantmentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_item_instance_enchantments_item_instances_ItemInstanceId",
                        column: x => x.ItemInstanceId,
                        principalTable: "item_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_item_instance_enchantments_EnchantmentDefinitionId",
                table: "item_instance_enchantments",
                column: "EnchantmentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_item_instances_GradeId",
                table: "item_instances",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_item_instances_ItemBlueprintId",
                table: "item_instances",
                column: "ItemBlueprintId");

            migrationBuilder.CreateIndex(
                name: "IX_item_instances_Origin_OriginRef",
                table: "item_instances",
                columns: new[] { "Origin", "OriginRef" });

            migrationBuilder.CreateIndex(
                name: "IX_item_instances_OwnerUserId",
                table: "item_instances",
                column: "OwnerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item_instance_enchantments");

            migrationBuilder.DropTable(
                name: "item_instances");
        }
    }
}
