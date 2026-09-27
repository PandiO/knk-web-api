using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddLootboxTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LootboxTokenId",
                table: "lootbox_claims",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "lootbox_token_grants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    LootboxTypeId = table.Column<int>(type: "int", nullable: false),
                    BoxStars = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    PermissionGroupId = table.Column<int>(type: "int", nullable: true),
                    KitId = table.Column<int>(type: "int", nullable: true),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lootbox_token_grants_kits_KitId",
                        column: x => x.KitId,
                        principalTable: "kits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_lootbox_token_grants_lootbox_types_LootboxTypeId",
                        column: x => x.LootboxTypeId,
                        principalTable: "lootbox_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lootbox_token_grants_permission_groups_PermissionGroupId",
                        column: x => x.PermissionGroupId,
                        principalTable: "permission_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "lootbox_tokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Token = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    LootboxTypeId = table.Column<int>(type: "int", nullable: false),
                    BoxGradeId = table.Column<int>(type: "int", nullable: false),
                    IssuedToUserId = table.Column<int>(type: "int", nullable: true),
                    IssuedReason = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IssueKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IssueIndex = table.Column<int>(type: "int", nullable: false),
                    IssuedByUserId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IssuedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    DeliveredAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RedeemedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    RedeemedByUserId = table.Column<int>(type: "int", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lootbox_tokens_grades_BoxGradeId",
                        column: x => x.BoxGradeId,
                        principalTable: "grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lootbox_tokens_lootbox_types_LootboxTypeId",
                        column: x => x.LootboxTypeId,
                        principalTable: "lootbox_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lootbox_tokens_users_IssuedByUserId",
                        column: x => x.IssuedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_lootbox_tokens_users_IssuedToUserId",
                        column: x => x.IssuedToUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_lootbox_tokens_users_RedeemedByUserId",
                        column: x => x.RedeemedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_claims_LootboxTokenId",
                table: "lootbox_claims",
                column: "LootboxTokenId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_token_grants_KitId",
                table: "lootbox_token_grants",
                column: "KitId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_token_grants_LootboxTypeId",
                table: "lootbox_token_grants",
                column: "LootboxTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_token_grants_PermissionGroupId",
                table: "lootbox_token_grants",
                column: "PermissionGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_tokens_BoxGradeId",
                table: "lootbox_tokens",
                column: "BoxGradeId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_tokens_IssuedByUserId",
                table: "lootbox_tokens",
                column: "IssuedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_tokens_IssuedToUserId",
                table: "lootbox_tokens",
                column: "IssuedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_tokens_IssueKey_IssueIndex",
                table: "lootbox_tokens",
                columns: new[] { "IssueKey", "IssueIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_tokens_LootboxTypeId",
                table: "lootbox_tokens",
                column: "LootboxTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_tokens_RedeemedByUserId",
                table: "lootbox_tokens",
                column: "RedeemedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_tokens_Token",
                table: "lootbox_tokens",
                column: "Token",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_lootbox_claims_lootbox_tokens_LootboxTokenId",
                table: "lootbox_claims",
                column: "LootboxTokenId",
                principalTable: "lootbox_tokens",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_lootbox_claims_lootbox_tokens_LootboxTokenId",
                table: "lootbox_claims");

            migrationBuilder.DropTable(
                name: "lootbox_token_grants");

            migrationBuilder.DropTable(
                name: "lootbox_tokens");

            migrationBuilder.DropIndex(
                name: "IX_lootbox_claims_LootboxTokenId",
                table: "lootbox_claims");

            migrationBuilder.DropColumn(
                name: "LootboxTokenId",
                table: "lootbox_claims");
        }
    }
}
