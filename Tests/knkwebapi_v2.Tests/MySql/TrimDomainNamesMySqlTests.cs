using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;
using knkwebapi_v2.Migrations;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// The TrimDomainNames statements on a real MySQL: REGEXP_REPLACE with the POSIX class strips spaces, tabs and
/// both line-break characters at either end, keeps the inside of a value, and skips clean rows.
/// </summary>
[Trait("Category", "requires-mysql")]
public class TrimDomainNamesMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public TrimDomainNamesMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    [MySqlFact]
    public async Task Up_TrimsTheStoredNamesAndDescriptions()
    {
        int dirtyTown, dirtyDistrict, cleanTown;
        await using (var ctx = _db.NewContext())
        {
            // Straight through the DbContext, as the rows were stored before DomainNameRules existed.
            var town = new Town { Name = " Cinix\r\n", Description = "\tThe capital \n", WgRegionId = "domain_t", WorldName = "world" };
            var clean = new Town { Name = "Oakhaven", Description = "keep\ninner newline", WgRegionId = "domain_c", WorldName = "world" };
            ctx.Towns.AddRange(town, clean);
            await ctx.SaveChangesAsync();
            var district = new District { Name = "Residential District\n", Description = "", TownId = town.Id, WgRegionId = "domain_d", WorldName = "world" };
            ctx.Districts.Add(district);
            await ctx.SaveChangesAsync();
            (dirtyTown, dirtyDistrict, cleanTown) = (town.Id, district.Id, clean.Id);

            foreach (var op in new TrimDomainNames().UpOperations.Cast<SqlOperation>())
            {
                await ctx.Database.ExecuteSqlRawAsync(op.Sql);
            }
        }

        await using (var ctx = _db.NewContext())
        {
            var rows = await ctx.Domains.AsNoTracking()
                .Where(d => d.Id == dirtyTown || d.Id == dirtyDistrict || d.Id == cleanTown)
                .ToDictionaryAsync(d => d.Id);

            Assert.Equal("Cinix", rows[dirtyTown].Name);
            Assert.Equal("The capital", rows[dirtyTown].Description);
            Assert.Equal("Residential District", rows[dirtyDistrict].Name);
            Assert.Equal("", rows[dirtyDistrict].Description);
            Assert.Equal("Oakhaven", rows[cleanTown].Name);
            Assert.Equal("keep\ninner newline", rows[cleanTown].Description);
        }
    }
}
