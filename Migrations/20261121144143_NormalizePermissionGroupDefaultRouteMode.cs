using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// PandiO/knk-workspace#852 follow-up (developer decision 2026-11-21): <c>PermissionGroup.DefaultRouteMode</c> is an
    /// enum (<see cref="knkwebapi_v2.Enums.RouteMode"/>) stored by name, so the lowercase mode words stored since
    /// <see cref="AddPermissionGroupDefaultRouteMode"/> are renamed to the enum names (<c>efficient</c> to
    /// <c>Efficient</c>). The column stays as it is (nullable <c>varchar(16)</c>); a group without a default stays null.
    /// Down turns the names back into the lowercase words.
    /// </summary>
    public partial class NormalizePermissionGroupDefaultRouteMode : Migration
    {
        /// <summary>The stored words and the enum names they become, in the enum's order.</summary>
        public static readonly (string Word, string Name)[] Renames =
        {
            ("shortest", "Shortest"),
            ("efficient", "Efficient"),
            ("preferred", "Preferred"),
        };

        /// <summary>One UPDATE per mode, matching the stored value in any case (under any collation).</summary>
        public static string RenameSql(string from, string to) =>
            $"UPDATE `permission_groups` SET `DefaultRouteMode` = '{to}' WHERE LOWER(`DefaultRouteMode`) = '{from.ToLowerInvariant()}';";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (word, name) in Renames)
            {
                migrationBuilder.Sql(RenameSql(word, name));
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (word, name) in Renames)
            {
                migrationBuilder.Sql(RenameSql(name, word));
            }
        }
    }
}
