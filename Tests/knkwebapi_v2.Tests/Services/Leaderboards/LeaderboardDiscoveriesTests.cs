using knkwebapi_v2.Models;
using knkwebapi_v2.Tests.Services.Statistics;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Leaderboards;

/// <summary>
/// The discoveries board counts only domains whose discovery type is enabled (after per-domain
/// overrides), like the player's /discoveries total (DiscoveryEnabledDomains, 2026-10-10 alignment).
/// </summary>
public class LeaderboardDiscoveriesTests : IDisposable
{
    private readonly StatisticsTestDb _db = new();

    public LeaderboardDiscoveriesTests()
    {
        var ctx = _db.Context;
        ctx.Streets.Add(new Street { Id = 1, Name = "Main" });
        ctx.Towns.Add(new Town { Id = 1, Name = "Rivia", Description = "", WgRegionId = "town_rivia" });
        ctx.Districts.Add(new District { Id = 2, Name = "Old Quarter", Description = "", WgRegionId = "district_oq", TownId = 1 });
        ctx.Structures.Add(new Structure { Id = 3, Name = "Smithy", Description = "", WgRegionId = "structure_smithy", StreetId = 1, DistrictId = 2 });
        ctx.Structures.Add(new Structure { Id = 4, Name = "Chapel", Description = "", WgRegionId = "structure_chapel", StreetId = 1, DistrictId = 2 });
        ctx.DiscoveryRewardRules.AddRange(
            new DiscoveryRewardRule { DomainType = DiscoveryRewardRule.Town, IsEnabled = true },
            new DiscoveryRewardRule { DomainType = DiscoveryRewardRule.District, IsEnabled = false },
            new DiscoveryRewardRule { DomainType = DiscoveryRewardRule.Structure, IsEnabled = false });
        // A per-domain override switches the Chapel back on.
        ctx.DomainDiscoveryOverrides.Add(new DomainDiscoveryOverride { DomainId = 4, IsEnabled = true });
        foreach (var domain in new[] { 1, 2, 3, 4 })
        {
            ctx.UserDomainDiscoveries.Add(new UserDomainDiscovery { UserId = 1, DomainId = domain, DiscoveredAt = StatisticsTestDb.Now.AddDays(-1) });
        }
        // A discovery of a domain that no longer exists.
        ctx.UserDomainDiscoveries.Add(new UserDomainDiscovery { UserId = 2, DomainId = 99, DiscoveredAt = StatisticsTestDb.Now.AddDays(-1) });
        ctx.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task OnlyEnabledDomains_Count()
    {
        var discoveries = await _db.LeaderboardRepository().GetDiscoveriesAsync();

        Assert.Equal(new[] { 1, 4 }, discoveries.Select(d => d.DomainId).OrderBy(d => d).ToArray());
        Assert.All(discoveries, d => Assert.Equal(1, d.UserId));
    }
}
