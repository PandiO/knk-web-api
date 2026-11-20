using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddSiegeWeaponUnlockMinTitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UnlockMinTitleLevel",
                table: "siege_weapon_templates",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_siege_weapon_templates_UnlockMinTitleLevel",
                table: "siege_weapon_templates",
                column: "UnlockMinTitleLevel");

            migrationBuilder.AddForeignKey(
                name: "FK_siege_weapon_templates_title_brackets_UnlockMinTitleLevel",
                table: "siege_weapon_templates",
                column: "UnlockMinTitleLevel",
                principalTable: "title_brackets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_siege_weapon_templates_title_brackets_UnlockMinTitleLevel",
                table: "siege_weapon_templates");

            migrationBuilder.DropIndex(
                name: "IX_siege_weapon_templates_UnlockMinTitleLevel",
                table: "siege_weapon_templates");

            migrationBuilder.DropColumn(
                name: "UnlockMinTitleLevel",
                table: "siege_weapon_templates");
        }
    }
}
