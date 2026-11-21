using knkwebapi_v2.Enums;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// NormalizePermissionGroupDefaultRouteMode on a real MySQL (PandiO/knk-workspace#852 follow-up, 2026-11-21): groups
/// that stored a lowercase mode word before the enum read back as that <see cref="RouteMode"/> after Up, a group
/// without a default stays null, and Down turns the names back into the words.
/// </summary>
[Trait("Category", "requires-mysql")]
public class NormalizePermissionGroupDefaultRouteModeMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private const string Previous = "20261121052426_AddTownLogisticsProfile";
    private readonly MySqlTestDatabase _db;

    public NormalizePermissionGroupDefaultRouteModeMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static Task<string?> Stored(KnKDbContext ctx, int id) =>
        ctx.Database.SqlQuery<string?>($"SELECT DefaultRouteMode AS Value FROM permission_groups WHERE Id = {id}").SingleAsync();

    [MySqlFact]
    public async Task Up_RenamesTheStoredWords_Down_RenamesThemBack()
    {
        int shortest, efficient, none;

        await using (var ctx = _db.NewContext())
        {
            var migrator = ctx.GetService<IMigrator>();
            await migrator.MigrateAsync(Previous);

            shortest = await PermissionGroupRows.InsertAsync(ctx, "RouteModeShortest", 51);
            efficient = await PermissionGroupRows.InsertAsync(ctx, "RouteModeEfficient", 52);
            none = await PermissionGroupRows.InsertAsync(ctx, "RouteModeNone", 53);
            await ctx.Database.ExecuteSqlInterpolatedAsync($"UPDATE permission_groups SET DefaultRouteMode = 'shortest' WHERE Id = {shortest};");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"UPDATE permission_groups SET DefaultRouteMode = 'efficient' WHERE Id = {efficient};");

            await migrator.MigrateAsync();

            Assert.Equal("Shortest", await Stored(ctx, shortest));
            Assert.Equal("Efficient", await Stored(ctx, efficient));
            Assert.Null(await Stored(ctx, none));
        }

        await using (var check = _db.NewContext())
        {
            var modes = await check.PermissionGroups.AsNoTracking()
                .Where(g => g.Id == shortest || g.Id == efficient || g.Id == none)
                .ToDictionaryAsync(g => g.Id, g => g.DefaultRouteMode);
            Assert.Equal(RouteMode.Shortest, modes[shortest]);
            Assert.Equal(RouteMode.Efficient, modes[efficient]);
            Assert.Null(modes[none]);
        }

        await using (var ctx = _db.NewContext())
        {
            var migrator = ctx.GetService<IMigrator>();
            await migrator.MigrateAsync(Previous);
            Assert.Equal("shortest", await Stored(ctx, shortest));
            Assert.Equal("efficient", await Stored(ctx, efficient));
            Assert.Null(await Stored(ctx, none));
            await migrator.MigrateAsync();
        }
    }
}
