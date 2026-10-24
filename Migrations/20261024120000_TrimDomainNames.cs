using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using knkwebapi_v2.Properties;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// Strips leading and trailing whitespace (spaces, tabs, newlines) from every domain name. Two districts were
    /// saved as "Merchant's District\n" and "Residential District\n", and the game server drew the newline as a
    /// missing-glyph box in its "You are now entering ..." message. <c>Domain.Name</c> now trims on write, so this
    /// only has to clean the rows saved before that. Data only, no schema change; nothing to undo.
    /// </summary>
    [DbContext(typeof(KnKDbContext))]
    [Migration("20261024120000_TrimDomainNames")]
    public partial class TrimDomainNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE domains SET Name = REGEXP_REPLACE(Name, '^[[:space:]]+|[[:space:]]+$', '') " +
                "WHERE Name <> REGEXP_REPLACE(Name, '^[[:space:]]+|[[:space:]]+$', '');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
