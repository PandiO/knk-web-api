using knkwebapi_v2.Models;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// Production world P2 (PandiO/knk-workspace#91) on MySQL: the District-xor-Town check constraint refuses a structure
/// with both or neither, and a street, district or town that a structure still uses can't be deleted (the optional FKs
/// keep the database refusal, EF does not null them).
/// </summary>
[Trait("Category", "requires-mysql")]
public class ProductionWorldMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public ProductionWorldMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private async Task<(int Town, int District, int Street)> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var town = new Town { Name = "Phoenix " + suffix, Description = "t", WgRegionId = "t_" + suffix, WorldName = "world" };
        ctx.Towns.Add(town);
        await ctx.SaveChangesAsync();
        var district = new District { Name = "Market " + suffix, Description = "d", WgRegionId = "d_" + suffix, WorldName = "world", TownId = town.Id };
        var street = new Street { Name = "Main " + suffix };
        ctx.Districts.Add(district);
        ctx.Streets.Add(street);
        await ctx.SaveChangesAsync();
        return (town.Id, district.Id, street.Id);
    }

    private static Structure NewStructure(int? districtId, int? townId, int? streetId) => new()
    {
        Name = "S " + Guid.NewGuid().ToString("N")[..8], Description = "s", WgRegionId = "s_" + Guid.NewGuid().ToString("N")[..8],
        WorldName = "world", DistrictId = districtId, TownId = townId, StreetId = streetId, HouseNumber = streetId.HasValue ? 1 : 0,
    };

    [MySqlFact]
    public async Task CheckConstraint_RefusesBothAndNeither_AndAcceptsEach()
    {
        var (town, district, street) = await SeedAsync();

        foreach (var (d, t, ok) in new[] { (district, (int?)null, true), (0, town, true), (district, town, false), (0, null, false) })
        {
            await using var ctx = _db.NewContext();
            ctx.Structures.Add(NewStructure(d > 0 ? d : null, t, d > 0 ? street : null));
            if (ok) await ctx.SaveChangesAsync();
            else
            {
                var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
                Assert.Contains("CK_structures_DistrictXorTown", ex.InnerException?.Message ?? ex.Message);
            }
        }
    }

    [MySqlFact]
    public async Task DeletingAStreetOrTownAStructureUses_IsRefused_AndTheStructureKeepsIt()
    {
        var (town, district, street) = await SeedAsync();
        int inDistrict, rural;
        await using (var ctx = _db.NewContext())
        {
            var a = NewStructure(district, null, street);
            var b = NewStructure(null, town, null);
            ctx.Structures.AddRange(a, b);
            await ctx.SaveChangesAsync();
            (inDistrict, rural) = (a.Id, b.Id);
        }

        await using (var ctx = _db.NewContext())
        {
            var loaded = await ctx.Streets.Include(s => s.Structures).SingleAsync(s => s.Id == street);
            ctx.Streets.Remove(loaded);
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }
        await using (var ctx = _db.NewContext())
        {
            ctx.Towns.Remove(await ctx.Towns.SingleAsync(t => t.Id == town));
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }

        await using (var ctx = _db.NewContext())
        {
            Assert.Equal(street, (await ctx.Structures.SingleAsync(s => s.Id == inDistrict)).StreetId);
            Assert.Equal(town, (await ctx.Structures.SingleAsync(s => s.Id == rural)).TownId);
        }
    }
}
