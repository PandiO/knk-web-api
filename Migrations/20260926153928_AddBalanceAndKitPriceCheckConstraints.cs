using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddBalanceAndKitPriceCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // KNG-22 (currency DESIGN.md §1.4 A4/A9). Rows written before these checks existed
            // (e.g. through the removed PUT api/Users/{id}/coins, which accepted any value) would
            // make ADD CONSTRAINT fail, so pull them into range first. Only out-of-range rows
            // change; on a healthy database these updates match nothing.
            migrationBuilder.Sql("UPDATE `users` SET `Coins` = LEAST(GREATEST(`Coins`, 0), 999999999) WHERE `Coins` < 0 OR `Coins` > 999999999;");
            migrationBuilder.Sql("UPDATE `users` SET `Gems` = LEAST(GREATEST(`Gems`, 0), 999999) WHERE `Gems` < 0 OR `Gems` > 999999;");
            migrationBuilder.Sql("UPDATE `users` SET `ExperiencePoints` = 0 WHERE `ExperiencePoints` < 0;");
            migrationBuilder.Sql("UPDATE `kits` SET `CostAmount` = 0 WHERE `CostAmount` < 0;");
            migrationBuilder.Sql("UPDATE `kits` SET `PremiumPriceGems` = 0 WHERE `PremiumPriceGems` < 0;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_users_Coins_Range",
                table: "users",
                sql: "`Coins` >= 0 AND `Coins` <= 999999999");

            migrationBuilder.AddCheckConstraint(
                name: "CK_users_ExperiencePoints_NonNegative",
                table: "users",
                sql: "`ExperiencePoints` >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_users_Gems_Range",
                table: "users",
                sql: "`Gems` >= 0 AND `Gems` <= 999999");

            migrationBuilder.AddCheckConstraint(
                name: "CK_kits_CostAmount_NonNegative",
                table: "kits",
                sql: "`CostAmount` IS NULL OR `CostAmount` >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_kits_PremiumPriceGems_NonNegative",
                table: "kits",
                sql: "`PremiumPriceGems` IS NULL OR `PremiumPriceGems` >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_users_Coins_Range",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_users_ExperiencePoints_NonNegative",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_users_Gems_Range",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_kits_CostAmount_NonNegative",
                table: "kits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_kits_PremiumPriceGems_NonNegative",
                table: "kits");
        }
    }
}
