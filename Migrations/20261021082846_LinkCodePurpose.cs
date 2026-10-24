using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class LinkCodePurpose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MinecraftName",
                table: "linkcodes",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "MinecraftUuid",
                table: "linkcodes",
                type: "varchar(36)",
                maxLength: 36,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "Purpose",
                table: "linkcodes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // KNG-208: until now a password-reset token was told apart from a link code only by its
            // length (a 64-character SHA-256 hash). Backfill so pending reset tokens stay valid.
            migrationBuilder.Sql("UPDATE linkcodes SET Purpose = 1 WHERE CHAR_LENGTH(Code) = 64;");

            migrationBuilder.CreateIndex(
                name: "IX_linkcodes_Purpose_Status_ExpiresAt",
                table: "linkcodes",
                columns: new[] { "Purpose", "Status", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_linkcodes_Purpose_Status_ExpiresAt",
                table: "linkcodes");

            migrationBuilder.DropColumn(
                name: "MinecraftName",
                table: "linkcodes");

            migrationBuilder.DropColumn(
                name: "MinecraftUuid",
                table: "linkcodes");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "linkcodes");
        }
    }
}
