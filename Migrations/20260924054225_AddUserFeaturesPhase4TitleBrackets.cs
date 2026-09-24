using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddUserFeaturesPhase4TitleBrackets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "title_brackets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MinExperience = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_title_brackets_MinExperience",
                table: "title_brackets",
                column: "MinExperience",
                unique: true);

            // Seed placeholder brackets at v1's reward-threshold crossing points (5/10/12/15,
            // confirmed docs/specs/user-features/DESIGN.md §7 item 10). Names are placeholder
            // content only, not a recovery of v1's real title names — those were never committed
            // to source (v1's Titles table was pure runtime DB content), so there is nothing to
            // port beyond the numeric thresholds themselves. Retunable later via the admin UI.
            migrationBuilder.InsertData(
                table: "title_brackets",
                columns: new[] { "Id", "Name", "MinExperience" },
                values: new object[,]
                {
                    { 1, "Novice", 0 },
                    { 2, "Apprentice", 5 },
                    { 3, "Journeyman", 10 },
                    { 4, "Veteran", 12 },
                    { 5, "Master", 15 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "title_brackets");
        }
    }
}
