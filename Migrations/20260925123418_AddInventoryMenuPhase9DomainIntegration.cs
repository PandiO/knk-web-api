using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryMenuPhase9DomainIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutoRefreshTicks",
                table: "menu_templates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRowTemplate",
                table: "menu_item_templates",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Phase",
                table: "menu_condition_bindings",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                // Hand-edited from the generated "": every existing condition keeps
                // Phase 6's click-time behaviour (InventoryMenu Phase 9, E5).
                defaultValue: "Click",
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoRefreshTicks",
                table: "menu_templates");

            migrationBuilder.DropColumn(
                name: "IsRowTemplate",
                table: "menu_item_templates");

            migrationBuilder.DropColumn(
                name: "Phase",
                table: "menu_condition_bindings");
        }
    }
}
