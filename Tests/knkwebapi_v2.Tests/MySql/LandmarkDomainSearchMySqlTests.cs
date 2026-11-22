using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// Landmarks as a Domain subtype, phase 2 (PandiO/knk-workspace#984) on a real MySQL: the domain search's exact type
/// filter (TPT type tests over <c>domains</c>, <c>structures</c>, <c>gate_structures</c> and <c>landmarks</c>) and the
/// landmark subtree filter translate and return what the InMemory tests expect, and the migrations seed the disabled
/// <c>Landmark</c> discovery rule.
/// </summary>
[Trait("Category", "requires-mysql")]
public class LandmarkDomainSearchMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public LandmarkDomainSearchMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    [MySqlFact]
    public async Task TheTypeFilter_AndTheSubtreeFilter_Translate()
    {
        var s = Guid.NewGuid().ToString("N")[..6];
        int palace, throne, well;
        await using (var ctx = _db.NewContext())
        {
            var town = new Town { Name = "Rivia " + s, Description = "", WgRegionId = "town_" + s, WorldName = "world" };
            ctx.Add(town);
            await ctx.SaveChangesAsync();
            ctx.AddRange(
                new Structure { Name = "Smithy " + s, Description = "", WgRegionId = "structure_" + s, TownId = town.Id, WorldName = "world" },
                new GateStructure { Name = "Gate " + s, Description = "", WgRegionId = "gate_" + s, TownId = town.Id, WorldName = "world" });
            var palaceLandmark = new Landmark { Name = "Palace " + s, Description = "", AreaKind = LandmarkAreaKind.OwnRegion,
                WgRegionId = "domain_p" + s, WorldName = "world", ParentDomainId = town.Id };
            var wellLandmark = new Landmark { Name = "Well " + s, Description = "", WorldName = "world" };
            ctx.AddRange(palaceLandmark, wellLandmark);
            await ctx.SaveChangesAsync();
            var throneLandmark = new Landmark { Name = "Throne " + s, Description = "", WorldName = "world", ParentDomainId = palaceLandmark.Id };
            ctx.Add(throneLandmark);
            await ctx.SaveChangesAsync();
            (palace, throne, well) = (palaceLandmark.Id, throneLandmark.Id, wellLandmark.Id);
        }

        async Task<List<string>> Search(params (string Key, string Value)[] filters)
        {
            await using var ctx = _db.NewContext();
            var result = await new DomainRepository(ctx).SearchAsync(new PagedQuery
            {
                PageSize = 50, SearchTerm = s, SortBy = "name", Filters = filters.ToDictionary(f => f.Key, f => f.Value)
            });
            Assert.Equal(result.Items.Count, result.TotalCount);
            return result.Items.Select(d => $"{d.GetType().Name}:{d.Name.Split(' ')[0]}").ToList();
        }

        Assert.Equal(new[] { "Landmark:Palace", "Landmark:Throne", "Landmark:Well" },
            await Search((DomainRepository.DomainTypeFilter, "Landmark")));
        Assert.Equal(new[] { "Structure:Smithy" }, await Search((DomainRepository.DomainTypeFilter, "structure")));
        Assert.Equal(new[] { "GateStructure:Gate", "Town:Rivia" }, await Search((DomainRepository.DomainTypeFilter, "Town,GateStructure")));
        Assert.Empty(await Search((DomainRepository.DomainTypeFilter, "Castle")));
        Assert.Equal(new[] { "Landmark:Well" },
            await Search((DomainRepository.DomainTypeFilter, "Landmark"), (DomainRepository.ExcludeSubtreeFilter, palace.ToString())));
        Assert.True(throne > 0 && well > 0);
    }

    [MySqlFact]
    public async Task TheMigrations_SeedTheLandmarkRule_Disabled()
    {
        await using var ctx = _db.NewContext();
        var rule = await ctx.DiscoveryRewardRules.AsNoTracking().SingleAsync(r => r.DomainType == "Landmark");
        Assert.False(rule.IsEnabled);
        Assert.False(rule.IncludeAncestors);
    }
}
