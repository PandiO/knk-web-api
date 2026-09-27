using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>Discovery reward rules, overrides and preview (docs/specs/domain-discovery/DESIGN.md §3.5).</summary>
public class DiscoveryConfigurationServiceTests : IDisposable
{
    private readonly KnKDbContext _db;
    private readonly DiscoveryConfigurationService _service;

    public DiscoveryConfigurationServiceTests()
    {
        _db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        _db.TitleBrackets.AddRange(DiscoveryRewardCalculatorTests.RealBrackets(idOffset: 1));
        _db.DiscoveryRewardRules.AddRange(
            DiscoveryRewardCalculatorTests.TownRule(),
            DiscoveryRewardCalculatorTests.DistrictRule(),
            DiscoveryRewardCalculatorTests.StructureRule());
        _db.Towns.Add(new Town { Id = 1, Name = "Rivia", Description = "", WgRegionId = "town_rivia" });
        _db.Streets.Add(new Street { Id = 1, Name = "Main" });
        _db.Districts.Add(new District { Id = 2, Name = "Old Quarter", Description = "", WgRegionId = "d", TownId = 1 });
        _db.Structures.Add(new Structure { Id = 3, Name = "Cathedral", Description = "", WgRegionId = "s", StreetId = 1, DistrictId = 2 });
        _db.Domains.Add(new Domain { Id = 9, Name = "Plain", Description = "", WgRegionId = "plain" });
        _db.SaveChanges();

        _service = new DiscoveryConfigurationService(new DiscoveryRepository(_db), new TitleService(new TitleBracketRepository(_db)));
    }

    public void Dispose() => _db.Dispose();

    private static UpdateDiscoveryRewardRuleDto Rule(decimal expMin = 1, decimal expMax = 4, decimal coinMin = 2, decimal coinMax = 8, int gemsMin = 5, int gemsMax = 15) => new()
    {
        IsEnabled = true, ExpUnitsMin = expMin, ExpUnitsMax = expMax, CoinSalaryHoursMin = coinMin, CoinSalaryHoursMax = coinMax,
        GemsMin = gemsMin, GemsMax = gemsMax, IncludeAncestors = false
    };

    [Fact]
    public async Task GetRules_ListsAllFourTypesTopDownAndAMissingRowAsDisabled()
    {
        var rules = await _service.GetRulesAsync();

        Assert.Equal(new[] { "Town", "District", "Structure", "GateStructure" }, rules.Select(r => r.DomainType));
        Assert.Equal((1m, 4m, 5, 15), (rules[0].ExpUnitsMin, rules[0].ExpUnitsMax, rules[0].GemsMin, rules[0].GemsMax));
        Assert.False(rules[3].IsEnabled); // no GateStructure row seeded in this test
    }

    [Fact]
    public async Task UpdateRule_SavesAndAcceptsAnyCasing()
    {
        var updated = await _service.UpdateRuleAsync("district", Rule(expMin: 0.25m, expMax: 1m, gemsMin: 0, gemsMax: 2));

        Assert.Equal(("District", 0.25m, 2), (updated.DomainType, updated.ExpUnitsMin, updated.GemsMax));
        Assert.Equal(0.25m, _db.DiscoveryRewardRules.AsNoTracking().Single(r => r.DomainType == "District").ExpUnitsMin);
    }

    [Fact]
    public async Task UpdateRule_CreatesAMissingRow()
    {
        await _service.UpdateRuleAsync("GateStructure", Rule());

        Assert.True(_db.DiscoveryRewardRules.AsNoTracking().Single(r => r.DomainType == "GateStructure").IsEnabled);
    }

    [Fact]
    public async Task UpdateRule_UnknownTypeIsNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdateRuleAsync("Domain", Rule()));
    }

    [Theory]
    [InlineData(4, 1, 2, 8, 5, 15)]     // exp min > max
    [InlineData(1, 4, 8, 2, 5, 15)]     // coin min > max
    [InlineData(1, 4, 2, 8, 15, 5)]     // gems min > max
    [InlineData(-1, 4, 2, 8, 5, 15)]    // negative
    [InlineData(1, 4, 2, 8, -5, 15)]
    [InlineData(1, 5000, 2, 8, 5, 15)]  // absurdly large
    [InlineData(1, 4, 2, 8, 5, 1000000)]
    public async Task UpdateRule_RejectsInvalidRanges(double expMin, double expMax, double coinMin, double coinMax, int gemsMin, int gemsMax)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateRuleAsync("Town",
            Rule((decimal)expMin, (decimal)expMax, (decimal)coinMin, (decimal)coinMax, gemsMin, gemsMax)));
        Assert.Equal(1m, _db.DiscoveryRewardRules.AsNoTracking().Single(r => r.DomainType == "Town").ExpUnitsMin);
    }

    [Fact]
    public async Task Override_UpsertListAndDelete()
    {
        var saved = await _service.UpsertOverrideAsync(3, new UpdateDomainDiscoveryOverrideDto { GemsMin = 2, GemsMax = 4 });
        Assert.Equal((3, "Cathedral", "Structure", (int?)2, (bool?)null), (saved.DomainId, saved.DomainName, saved.DomainType, saved.GemsMin, saved.IsEnabled));

        var replaced = await _service.UpsertOverrideAsync(3, new UpdateDomainDiscoveryOverrideDto { IsEnabled = false });
        Assert.Null(replaced.GemsMin); // PUT replaces the whole override

        var list = await _service.GetOverridesAsync();
        Assert.Equal((3, (bool?)false), (Assert.Single(list).DomainId, list[0].IsEnabled));

        Assert.True(await _service.DeleteOverrideAsync(3));
        Assert.False(await _service.DeleteOverrideAsync(3));
        Assert.Empty(_db.DomainDiscoveryOverrides);
    }

    [Fact]
    public async Task Override_IsValidatedMergedWithTheTypeRule()
    {
        // Structure gems are 0-0, so a min of 2 without a max would roll min > max.
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpsertOverrideAsync(3, new UpdateDomainDiscoveryOverrideDto { GemsMin = 2 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpsertOverrideAsync(3, new UpdateDomainDiscoveryOverrideDto { ExpUnitsMax = -1 }));
        Assert.Empty(_db.DomainDiscoveryOverrides);
    }

    [Fact]
    public async Task Override_ForSomethingThatIsNotADiscoverableDomainIsNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpsertOverrideAsync(404, new UpdateDomainDiscoveryOverrideDto()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpsertOverrideAsync(9, new UpdateDomainDiscoveryOverrideDto()));
    }

    [Fact]
    public async Task Preview_ShowsEveryTitleAtMultiplierOne()
    {
        var preview = await _service.PreviewAsync("town", null);

        Assert.Equal("Town", preview.DomainType);
        Assert.Equal(19, preview.Rows.Count);
        var serf = preview.Rows[0];
        Assert.Equal(("Serf", 25, 650, 25, 100, 1300, 5200, 5, 15),
            (serf.TitleName, serf.ExpUnit, serf.Salary, serf.ExpMin, serf.ExpMax, serf.CoinsMin, serf.CoinsMax, serf.GemsMin, serf.GemsMax));
        var count = preview.Rows.Single(r => r.TitleName == "Count");
        Assert.Equal((100, 400, 20000, 80000), (count.ExpMin, count.ExpMax, count.CoinsMin, count.CoinsMax));
    }

    [Fact]
    public async Task Preview_ForADomainAppliesItsOverrideAndItsOwnType()
    {
        await _service.UpsertOverrideAsync(3, new UpdateDomainDiscoveryOverrideDto { GemsMin = 7, GemsMax = 9 });

        var preview = await _service.PreviewAsync("Town", 3);

        Assert.Equal(("Structure", (int?)3, 7, 9), (preview.DomainType, preview.DomainId, preview.Rule.GemsMin, preview.Rule.GemsMax));
        Assert.All(preview.Rows, r => Assert.Equal((7, 9), (r.GemsMin, r.GemsMax)));
    }

    [Fact]
    public async Task Preview_NeedsATypeOrADomain()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PreviewAsync(null, null));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.PreviewAsync("Castle", null));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.PreviewAsync(null, 404));
    }
}
