using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddSalaryOfflinePayoutMaxHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // KNG-16: salary for a gap between payouts decays logarithmically and stops counting
            // after this many hours (720 = 30 days). The default fills the existing singleton row.
            migrationBuilder.AddColumn<int>(
                name: "OfflinePayoutMaxHours",
                table: "salary_configurations",
                type: "int",
                nullable: false,
                defaultValue: 720);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OfflinePayoutMaxHours",
                table: "salary_configurations");
        }
    }
}
