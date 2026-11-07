using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddSiegeSurvivalMysteryBoxRolls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "siege_survival_mystery_box_rolls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RollId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SiegeMatchId = table.Column<int>(type: "int", nullable: false),
                    SurvivalMysteryBoxPointId = table.Column<int>(type: "int", nullable: false),
                    SurvivalMysteryBoxPoolId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Variant = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SurvivalMysteryBoxEntryId = table.Column<int>(type: "int", nullable: false),
                    EntryJson = table.Column<string>(type: "longtext", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RolledEnchantmentsJson = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReelJson = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PriceCharged = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RolledAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ClaimedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ForfeitedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ForfeitReason = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_survival_mystery_box_rolls_siege_matches_SiegeMatchId",
                        column: x => x.SiegeMatchId,
                        principalTable: "siege_matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_siege_survival_mystery_box_rolls_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "siege_survival_mystery_box_deliveries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SurvivalMysteryBoxRollId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeliveredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_siege_survival_mystery_box_deliveries_siege_survival_mystery~",
                        column: x => x.SurvivalMysteryBoxRollId,
                        principalTable: "siege_survival_mystery_box_rolls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_mystery_box_deliveries_SurvivalMysteryBoxRoll~",
                table: "siege_survival_mystery_box_deliveries",
                column: "SurvivalMysteryBoxRollId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_mystery_box_rolls_RollId",
                table: "siege_survival_mystery_box_rolls",
                column: "RollId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_mystery_box_rolls_SiegeMatchId_RolledAt",
                table: "siege_survival_mystery_box_rolls",
                columns: new[] { "SiegeMatchId", "RolledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_mystery_box_rolls_SiegeMatchId_UserId",
                table: "siege_survival_mystery_box_rolls",
                columns: new[] { "SiegeMatchId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_siege_survival_mystery_box_rolls_UserId",
                table: "siege_survival_mystery_box_rolls",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "siege_survival_mystery_box_deliveries");

            migrationBuilder.DropTable(
                name: "siege_survival_mystery_box_rolls");
        }
    }
}
