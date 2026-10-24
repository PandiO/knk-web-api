using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;
using knkwebapi_v2.Migrations;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// The TrimDomainNames statement on a real MySQL: REGEXP_REPLACE with the POSIX class strips spaces, tabs and
/// both line-break characters at either end of domains.Name, keeps the inside of a value, leaves Description and
/// clean rows alone.
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
    public async Task Up_TrimsTheStoredNames()
    {
        int dirtyTown, dirtyDistrict, cleanTown;
        await using (var ctx = _db.NewContext())
        {
            // Domain.Name trims in its setter now, so the rows are stored the way the old API stored them: raw SQL.
            var town = new Town { Name = "Cinix", Description = "\tThe capital \n", WgRegionId = "domain_t", WorldName = "world" };
            var clean = new Town { Name = "Oakhaven", Description = "keep\ninner newline", WgRegionId = "domain_c", WorldName = "world" };
            ctx.Towns.AddRange(town, clean);
            await ctx.SaveChangesAsync();
            var district = new District { Name = "Residential District", Description = "", TownId = town.Id, WgRegionId = "domain_d", WorldName = "world" };
            ctx.Districts.Add(district);
            await ctx.SaveChangesAsync();
            (dirtyTown, dirtyDistrict, cleanTown) = (town.Id, district.Id, clean.Id);
            await ctx.Database.ExecuteSqlInterpolatedAsync($"UPDATE domains SET Name = {" Cinix\r\n"} WHERE Id = {dirtyTown}");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"UPDATE domains SET Name = {"Residential District\n"} WHERE Id = {dirtyDistrict}");

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
            Assert.Equal("\tThe capital \n", rows[dirtyTown].Description);
            Assert.Equal("Residential District", rows[dirtyDistrict].Name);
            Assert.Equal("", rows[dirtyDistrict].Description);
            Assert.Equal("Oakhaven", rows[cleanTown].Name);
            Assert.Equal("keep\ninner newline", rows[cleanTown].Description);
        }
    }
}
