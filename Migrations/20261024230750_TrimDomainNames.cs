using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class TrimDomainNames : Migration
    {
        /// <summary>
        /// The columns the API trims on every create/update since this migration (DomainNameRules).
        /// </summary>
        public static readonly string[] Columns = { "Name", "Description" };

        /// <summary>
        /// One data-fix statement: strips leading/trailing whitespace, tabs and line breaks from the column of every
        /// <c>domains</c> row that has any. MySQL's <c>TRIM()</c> removes spaces only, hence the regex (MySQL 8 ICU and
        /// MariaDB PCRE both take the POSIX class); the WHERE keeps clean rows untouched.
        /// </summary>
        public static string TrimSql(string column) =>
            $"UPDATE domains SET `{column}` = REGEXP_REPLACE(`{column}`, '^[[:space:]]+|[[:space:]]+$', '') " +
            $"WHERE `{column}` REGEXP '^[[:space:]]|[[:space:]]$';";

        /// <summary>
        /// Data only: the dev database held districts named "Residential District\n" (a newline the form's textarea
        /// let through), which the action bar rendered as a missing glyph. The services refuse the whitespace from
        /// now on; this cleans the rows stored before.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var column in Columns)
            {
                migrationBuilder.Sql(TrimSql(column));
            }
        }

        /// <summary>Nothing to undo: the stripped whitespace was never meant to be there.</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
