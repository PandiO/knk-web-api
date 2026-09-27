using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrencyAlertDedupKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DedupKey",
                table: "currency_alerts",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "currency_alerts",
                type: "varchar(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "",
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_currency_alerts_Rule_DedupKey_CreatedAt",
                table: "currency_alerts",
                columns: new[] { "Rule", "DedupKey", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_currency_alerts_Rule_DedupKey_CreatedAt",
                table: "currency_alerts");

            migrationBuilder.DropColumn(
                name: "DedupKey",
                table: "currency_alerts");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "currency_alerts");
        }
    }
}
