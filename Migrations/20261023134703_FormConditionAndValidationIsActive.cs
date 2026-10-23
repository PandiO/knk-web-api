using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class FormConditionAndValidationIsActive : Migration
    {
        /// <summary>
        /// KNG-186: the DTOs carried an IsActive the models never had, so the API sent false for every
        /// step condition and field validation and the wizard skipped them all. The default is true
        /// (not the scaffolded false) so every existing row stays active.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "StepConditions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "FieldValidations",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "StepConditions");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "FieldValidations");
        }
    }
}
