using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddLootboxWorldPickup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceSpawnId",
                table: "lootbox_tokens",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_lootbox_tokens_SourceSpawnId",
                table: "lootbox_tokens",
                column: "SourceSpawnId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_lootbox_tokens_lootbox_spawns_SourceSpawnId",
                table: "lootbox_tokens",
                column: "SourceSpawnId",
                principalTable: "lootbox_spawns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_lootbox_tokens_lootbox_spawns_SourceSpawnId",
                table: "lootbox_tokens");

            migrationBuilder.DropIndex(
                name: "IX_lootbox_tokens_SourceSpawnId",
                table: "lootbox_tokens");

            migrationBuilder.DropColumn(
                name: "SourceSpawnId",
                table: "lootbox_tokens");
        }
    }
}
