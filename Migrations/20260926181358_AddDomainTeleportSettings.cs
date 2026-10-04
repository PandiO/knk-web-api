using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddDomainTeleportSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "TeleportEnabled",
                table: "domains",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TeleportMinPremiumGroupId",
                table: "domains",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeleportMinTitleBracketId",
                table: "domains",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeleportPriceGems",
                table: "domains",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "TeleportRequiresDiscovery",
                table: "domains",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_domains_TeleportMinPremiumGroupId",
                table: "domains",
                column: "TeleportMinPremiumGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_domains_TeleportMinTitleBracketId",
                table: "domains",
                column: "TeleportMinTitleBracketId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_domains_TeleportPriceGems_Range",
                table: "domains",
                sql: "`TeleportPriceGems` >= 0 AND `TeleportPriceGems` <= 999999");

            migrationBuilder.AddForeignKey(
                name: "FK_domains_permission_groups_TeleportMinPremiumGroupId",
                table: "domains",
                column: "TeleportMinPremiumGroupId",
                principalTable: "permission_groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_domains_title_brackets_TeleportMinTitleBracketId",
                table: "domains",
                column: "TeleportMinTitleBracketId",
                principalTable: "title_brackets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_domains_permission_groups_TeleportMinPremiumGroupId",
                table: "domains");

            migrationBuilder.DropForeignKey(
                name: "FK_domains_title_brackets_TeleportMinTitleBracketId",
                table: "domains");

            migrationBuilder.DropIndex(
                name: "IX_domains_TeleportMinPremiumGroupId",
                table: "domains");

            migrationBuilder.DropIndex(
                name: "IX_domains_TeleportMinTitleBracketId",
                table: "domains");

            migrationBuilder.DropCheckConstraint(
                name: "CK_domains_TeleportPriceGems_Range",
                table: "domains");

            migrationBuilder.DropColumn(
                name: "TeleportEnabled",
                table: "domains");

            migrationBuilder.DropColumn(
                name: "TeleportMinPremiumGroupId",
                table: "domains");

            migrationBuilder.DropColumn(
                name: "TeleportMinTitleBracketId",
                table: "domains");

            migrationBuilder.DropColumn(
                name: "TeleportPriceGems",
                table: "domains");

            migrationBuilder.DropColumn(
                name: "TeleportRequiresDiscovery",
                table: "domains");
        }
    }
}
