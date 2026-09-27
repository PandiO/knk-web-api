using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class RouteBalancesThroughLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Gems",
                table: "users",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "ExperiencePoints",
                table: "users",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "Coins",
                table: "users",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<long>(
                name: "SignupGrant",
                table: "currency_policies",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Currency Phase 2: new accounts are inserted at 0 and get their starting balance as a
            // SIGNUP_GRANT ledger posting — the amounts the old User defaults gave (250 coins, 50 gems).
            migrationBuilder.Sql("UPDATE `currency_policies` SET `SignupGrant` = 250 WHERE `Currency` = 0;");
            migrationBuilder.Sql("UPDATE `currency_policies` SET `SignupGrant` = 50 WHERE `Currency` = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignupGrant",
                table: "currency_policies");

            migrationBuilder.AlterColumn<int>(
                name: "Gems",
                table: "users",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "ExperiencePoints",
                table: "users",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "Coins",
                table: "users",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0);
        }
    }
}
