using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigurationDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DraftJson",
                table: "FormConfigurations",
                type: "longtext",
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "DraftSavedAt",
                table: "FormConfigurations",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDraft",
                table: "FormConfigurations",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "FormConfigurations",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DraftJson",
                table: "DisplayConfigurations",
                type: "longtext",
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "DraftSavedAt",
                table: "DisplayConfigurations",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "DisplayConfigurations",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DraftJson",
                table: "FormConfigurations");

            migrationBuilder.DropColumn(
                name: "DraftSavedAt",
                table: "FormConfigurations");

            migrationBuilder.DropColumn(
                name: "IsDraft",
                table: "FormConfigurations");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "FormConfigurations");

            migrationBuilder.DropColumn(
                name: "DraftJson",
                table: "DisplayConfigurations");

            migrationBuilder.DropColumn(
                name: "DraftSavedAt",
                table: "DisplayConfigurations");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "DisplayConfigurations");
        }
    }
}
