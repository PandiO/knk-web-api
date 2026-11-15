using knkwebapi_v2.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// PandiO/knk-workspace#409 on a real MySQL: rolling DropTownStreet back recreates TownStreet and fills it with one row
/// per distinct (street, town) pair of the districts' streets; migrating forward again drops it.
/// </summary>
[Trait("Category", "requires-mysql")]
public class DropTownStreetMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private const string BeforeDropTownStreet = "20261113161725_SeedStaffFreezeNodes";
    private readonly MySqlTestDatabase _db;

    public DropTownStreetMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static async Task<bool> TableExistsAsync(DbContext ctx)
    {
        var count = await ctx.Database.SqlQueryRaw<long>(
            "SELECT COUNT(*) AS `Value` FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'TownStreet'")
            .SingleAsync();
        return count > 0;
    }

    [MySqlFact]
    public async Task Down_RefillsTownStreetFromTheDistricts_AndUpDropsItAgain()
    {
        int cinix, oakhaven, main, harbour, oak;
        await using (var ctx = _db.NewContext())
        {
            Assert.False(await TableExistsAsync(ctx));

            var mainStreet = new Street { Name = "Main Street" };
            var harbourRoad = new Street { Name = "Harbour Road" };
            var oakAvenue = new Street { Name = "Oak Avenue" };
            var town1 = new Town { Name = "Cinix", Description = "", WgRegionId = "domain_ts1", WorldName = "world" };
            var town2 = new Town { Name = "Oakhaven", Description = "", WgRegionId = "domain_ts2", WorldName = "world" };
            ctx.Towns.AddRange(town1, town2);
            await ctx.SaveChangesAsync();
            ctx.Districts.AddRange(
                new District { Name = "Old Quarter", Description = "", WgRegionId = "domain_ts3", WorldName = "world", TownId = town1.Id, Streets = { mainStreet, harbourRoad } },
                new District { Name = "Harbour", Description = "", WgRegionId = "domain_ts4", WorldName = "world", TownId = town1.Id, Streets = { harbourRoad } },
                new District { Name = "Market", Description = "", WgRegionId = "domain_ts5", WorldName = "world", TownId = town2.Id, Streets = { oakAvenue, harbourRoad } });
            await ctx.SaveChangesAsync();
            (cinix, oakhaven, main, harbour, oak) = (town1.Id, town2.Id, mainStreet.Id, harbourRoad.Id, oakAvenue.Id);

            await ctx.GetService<IMigrator>().MigrateAsync(BeforeDropTownStreet);
        }

        await using (var ctx = _db.NewContext())
        {
            Assert.True(await TableExistsAsync(ctx));
            var rows = await ctx.Database.SqlQueryRaw<string>(
                    "SELECT CONCAT(`StreetId`, ':', `TownId`) AS `Value` FROM `TownStreet`")
                .ToListAsync();

            Assert.Equal(
                new[] { $"{main}:{cinix}", $"{harbour}:{cinix}", $"{harbour}:{oakhaven}", $"{oak}:{oakhaven}" }.OrderBy(r => r),
                rows.OrderBy(r => r));

            await ctx.Database.MigrateAsync();
            Assert.False(await TableExistsAsync(ctx));
        }
    }
}
