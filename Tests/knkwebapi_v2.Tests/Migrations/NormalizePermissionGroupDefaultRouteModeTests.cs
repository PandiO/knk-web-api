using knkwebapi_v2.Enums;
using knkwebapi_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace knkwebapi_v2.Tests.Migrations;

/// <summary>
/// PandiO/knk-workspace#852 follow-up (developer decision 2026-11-21): the data migration renames the stored lowercase
/// mode words to the <see cref="RouteMode"/> names and changes nothing else; Down renames them back.
/// The real-MySQL run is <c>NormalizePermissionGroupDefaultRouteModeMySqlTests</c>.
/// </summary>
public class NormalizePermissionGroupDefaultRouteModeTests
{
    [Fact]
    public void Renames_CoverEveryRouteModeInTheEnumsOrder()
    {
        Assert.Equal(Enum.GetNames<RouteMode>(), NormalizePermissionGroupDefaultRouteMode.Renames.Select(r => r.Name));
        Assert.All(NormalizePermissionGroupDefaultRouteMode.Renames, r => Assert.Equal(r.Name.ToLowerInvariant(), r.Word));
    }

    [Fact]
    public void Up_OnlyRenamesTheWordsToTheEnumNames()
    {
        var sql = new NormalizePermissionGroupDefaultRouteMode().UpOperations.Select(o => Assert.IsType<SqlOperation>(o).Sql).ToList();

        Assert.Equal(new[]
        {
            "UPDATE `permission_groups` SET `DefaultRouteMode` = 'Shortest' WHERE LOWER(`DefaultRouteMode`) = 'shortest';",
            "UPDATE `permission_groups` SET `DefaultRouteMode` = 'Efficient' WHERE LOWER(`DefaultRouteMode`) = 'efficient';",
            "UPDATE `permission_groups` SET `DefaultRouteMode` = 'Preferred' WHERE LOWER(`DefaultRouteMode`) = 'preferred';",
        }, sql);
    }

    [Fact]
    public void Down_RenamesTheEnumNamesBackToTheWords()
    {
        var sql = new NormalizePermissionGroupDefaultRouteMode().DownOperations.Select(o => Assert.IsType<SqlOperation>(o).Sql).ToList();

        Assert.Equal(new[]
        {
            "UPDATE `permission_groups` SET `DefaultRouteMode` = 'shortest' WHERE LOWER(`DefaultRouteMode`) = 'shortest';",
            "UPDATE `permission_groups` SET `DefaultRouteMode` = 'efficient' WHERE LOWER(`DefaultRouteMode`) = 'efficient';",
            "UPDATE `permission_groups` SET `DefaultRouteMode` = 'preferred' WHERE LOWER(`DefaultRouteMode`) = 'preferred';",
        }, sql);
    }
}
