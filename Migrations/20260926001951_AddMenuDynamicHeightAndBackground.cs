using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuDynamicHeightAndBackground : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackgroundMaterial",
                table: "menu_templates",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "MinHeight",
                table: "menu_templates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinHeight",
                table: "menu_section_templates",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackgroundMaterial",
                table: "menu_templates");

            migrationBuilder.DropColumn(
                name: "MinHeight",
                table: "menu_templates");

            migrationBuilder.DropColumn(
                name: "MinHeight",
                table: "menu_section_templates");
        }
    }
}
