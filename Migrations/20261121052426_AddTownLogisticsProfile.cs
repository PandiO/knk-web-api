using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddTownLogisticsProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogisticsSpecialization",
                table: "towns",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "LogisticsTownSize",
                table: "towns",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LogisticsSpecialization",
                table: "towns");

            migrationBuilder.DropColumn(
                name: "LogisticsTownSize",
                table: "towns");
        }
    }
}
