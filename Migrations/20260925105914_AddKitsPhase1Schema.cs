using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddKitsPhase1Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "kits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HelmetId = table.Column<int>(type: "int", nullable: true),
                    ChestplateId = table.Column<int>(type: "int", nullable: true),
                    LeggingsId = table.Column<int>(type: "int", nullable: true),
                    BootsId = table.Column<int>(type: "int", nullable: true),
                    ShieldId = table.Column<int>(type: "int", nullable: true),
                    HandId = table.Column<int>(type: "int", nullable: true),
                    MinTitleBracketId = table.Column<int>(type: "int", nullable: true),
                    RequiredPermissionGroupId = table.Column<int>(type: "int", nullable: true),
                    RequiredPermissionNode = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GrantOnFirstJoin = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CooldownSeconds = table.Column<int>(type: "int", nullable: false),
                    CostAmount = table.Column<int>(type: "int", nullable: true),
                    CostCurrency = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsSinglePurchasePremium = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PremiumPriceGems = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_kits_item_blueprints_HelmetId",
                        column: x => x.HelmetId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_kits_item_blueprints_ChestplateId",
                        column: x => x.ChestplateId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_kits_item_blueprints_LeggingsId",
                        column: x => x.LeggingsId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_kits_item_blueprints_BootsId",
                        column: x => x.BootsId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_kits_item_blueprints_ShieldId",
                        column: x => x.ShieldId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_kits_item_blueprints_HandId",
                        column: x => x.HandId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_kits_title_brackets_MinTitleBracketId",
                        column: x => x.MinTitleBracketId,
                        principalTable: "title_brackets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_kits_permission_groups_RequiredPermissionGroupId",
                        column: x => x.RequiredPermissionGroupId,
                        principalTable: "permission_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_kits_HelmetId",
                table: "kits",
                column: "HelmetId");

            migrationBuilder.CreateIndex(
                name: "IX_kits_ChestplateId",
                table: "kits",
                column: "ChestplateId");

            migrationBuilder.CreateIndex(
                name: "IX_kits_LeggingsId",
                table: "kits",
                column: "LeggingsId");

            migrationBuilder.CreateIndex(
                name: "IX_kits_BootsId",
                table: "kits",
                column: "BootsId");

            migrationBuilder.CreateIndex(
                name: "IX_kits_ShieldId",
                table: "kits",
                column: "ShieldId");

            migrationBuilder.CreateIndex(
                name: "IX_kits_HandId",
                table: "kits",
                column: "HandId");

            migrationBuilder.CreateIndex(
                name: "IX_kits_MinTitleBracketId",
                table: "kits",
                column: "MinTitleBracketId");

            migrationBuilder.CreateIndex(
                name: "IX_kits_RequiredPermissionGroupId",
                table: "kits",
                column: "RequiredPermissionGroupId");

            migrationBuilder.CreateTable(
                name: "kit_contents",
                columns: table => new
                {
                    KitId = table.Column<int>(type: "int", nullable: false),
                    SlotIndex = table.Column<int>(type: "int", nullable: false),
                    ItemBlueprintId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kit_contents", x => new { x.KitId, x.SlotIndex });
                    table.ForeignKey(
                        name: "FK_kit_contents_kits_KitId",
                        column: x => x.KitId,
                        principalTable: "kits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_kit_contents_item_blueprints_ItemBlueprintId",
                        column: x => x.ItemBlueprintId,
                        principalTable: "item_blueprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_kit_contents_ItemBlueprintId",
                table: "kit_contents",
                column: "ItemBlueprintId");

            migrationBuilder.CreateTable(
                name: "kit_claims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    KitId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClaimedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kit_claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_kit_claims_kits_KitId",
                        column: x => x.KitId,
                        principalTable: "kits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_kit_claims_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_kit_claims_KitId_UserId_ClaimedAt",
                table: "kit_claims",
                columns: new[] { "KitId", "UserId", "ClaimedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_kit_claims_UserId",
                table: "kit_claims",
                column: "UserId");

            migrationBuilder.CreateTable(
                name: "kit_purchases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    KitId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PurchasedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    GemsPaid = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kit_purchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_kit_purchases_kits_KitId",
                        column: x => x.KitId,
                        principalTable: "kits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_kit_purchases_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_kit_purchases_KitId_UserId",
                table: "kit_purchases",
                columns: new[] { "KitId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kit_purchases_UserId",
                table: "kit_purchases",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kit_purchases");

            migrationBuilder.DropTable(
                name: "kit_claims");

            migrationBuilder.DropTable(
                name: "kit_contents");

            migrationBuilder.DropTable(
                name: "kits");
        }
    }
}
