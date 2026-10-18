using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddFormFieldSelectionLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanCreate",
                table: "FormFields",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IncrementValue",
                table: "FormFields",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxSelection",
                table: "FormFields",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinSelection",
                table: "FormFields",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanCreate",
                table: "FormFields");

            migrationBuilder.DropColumn(
                name: "IncrementValue",
                table: "FormFields");

            migrationBuilder.DropColumn(
                name: "MaxSelection",
                table: "FormFields");

            migrationBuilder.DropColumn(
                name: "MinSelection",
                table: "FormFields");
        }
    }
}
