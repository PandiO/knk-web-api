using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddItemIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VanillaMaterialMatchRefId",
                table: "item_blueprints",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "item_conversion_stats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Day = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Source = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Outcome = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MaterialKey = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_item_blueprints_VanillaMaterialMatchRefId",
                table: "item_blueprints",
                column: "VanillaMaterialMatchRefId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_item_conversion_stats_Day_Source_Outcome_MaterialKey",
                table: "item_conversion_stats",
                columns: new[] { "Day", "Source", "Outcome", "MaterialKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_item_blueprints_minecraftmaterialrefs_VanillaMaterialMatchRe~",
                table: "item_blueprints",
                column: "VanillaMaterialMatchRefId",
                principalTable: "minecraftmaterialrefs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_item_blueprints_minecraftmaterialrefs_VanillaMaterialMatchRe~",
                table: "item_blueprints");

            migrationBuilder.DropTable(
                name: "item_conversion_stats");

            migrationBuilder.DropIndex(
                name: "IX_item_blueprints_VanillaMaterialMatchRefId",
                table: "item_blueprints");

            migrationBuilder.DropColumn(
                name: "VanillaMaterialMatchRefId",
                table: "item_blueprints");
        }
    }
}
