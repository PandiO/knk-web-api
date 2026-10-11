using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-111: domains from before worlds were stored get one from the game server's region report, their Location or
/// their parent, only where those agree; region lookups by world pick the domain of that world.
/// </summary>
public class DomainWorldBackfillTests : IDisposable
{
    private readonly KnKDbContext _context;
    private readonly DomainWorldBackfill _backfill;

    public DomainWorldBackfillTests()
    {
        _context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _backfill = new DomainWorldBackfill(_context, NullLogger<DomainWorldBackfill>.Instance);
    }

    public void Dispose() => _context.Dispose();

    private static DomainWorldBackfillRequestDto Report(params (string Region, string[] Worlds)[] regions) => new()
    {
        Regions = regions.Select(r => new DomainRegionWorldsDto { WgRegionId = r.Region, Worlds = r.Worlds.ToList() }).ToList()
    };

    [Fact]
    public async Task ARegionFoundInOneWorldFillsItsDomainAndItsChildren()
    {
        _context.Towns.Add(new Town { Id = 1, Name = "T", Description = "", WgRegionId = "town_1" });
        _context.Districts.Add(new District { Id = 2, Name = "D", Description = "", WgRegionId = "dup", TownId = 1 });
        await _context.SaveChangesAsync();

        // The district's region exists in both worlds: its town decides.
        var result = await _backfill.ApplyAsync(Report(("town_1", new[] { "hub" }), ("dup", new[] { "hub", "gameplay" })));

        Assert.Equal(2, result.Updated);
        Assert.Empty(result.Unresolved);
        Assert.Equal("hub", (await _context.Domains.FindAsync(1))!.WorldName);
        Assert.Equal("hub", (await _context.Domains.FindAsync(2))!.WorldName);
    }

    [Fact]
    public async Task ConflictingOrMissingSourcesAreLeftForAnAdmin()
    {
        _context.Locations.Add(new Location { Id = 50, Name = "spawn", World = "gameplay" });
        _context.Towns.Add(new Town { Id = 1, Name = "Mismatch", Description = "", WgRegionId = "town_1", LocationId = 50 });
        _context.Towns.Add(new Town { Id = 3, Name = "Ambiguous", Description = "", WgRegionId = "town_3" });
        await _context.SaveChangesAsync();

        var result = await _backfill.ApplyAsync(Report(("town_1", new[] { "hub" }), ("town_3", new[] { "hub", "gameplay" })));

        Assert.Equal(0, result.Updated);
        Assert.Equal(new[] { 1, 3 }, result.Unresolved.Select(u => u.Id));
        Assert.Equal(new[] { "hub", "gameplay" }, result.Unresolved[1].CandidateWorlds);
        Assert.Equal("Town", result.Unresolved[0].DomainType);
    }

    [Fact]
    public async Task ARegionInNoReportedWorldTakesItsParentsWorldButNotAContradictingLocation()
    {
        _context.Towns.Add(new Town { Id = 1, Name = "T", Description = "", WgRegionId = "town_1", WorldName = "hub" });
        _context.Locations.Add(new Location { Id = 50, Name = "spawn", World = "gameplay" });
        _context.Districts.Add(new District { Id = 2, Name = "Unbuilt", Description = "", WgRegionId = "gone", TownId = 1 });
        _context.Districts.Add(new District { Id = 3, Name = "Odd", Description = "", WgRegionId = "odd", TownId = 1, LocationId = 50 });
        await _context.SaveChangesAsync();

        var result = await _backfill.ApplyAsync(Report(("gone", Array.Empty<string>())));

        Assert.Equal(1, result.Updated);
        Assert.Equal("hub", (await _context.Domains.FindAsync(2))!.WorldName);
        Assert.Equal(3, Assert.Single(result.Unresolved).Id);
    }

    [Fact]
    public async Task ADomainThatAlreadyHasAWorldIsNeverChanged()
    {
        _context.Towns.Add(new Town { Id = 1, Name = "T", Description = "", WgRegionId = "town_1", WorldName = "world" });
        await _context.SaveChangesAsync();

        var result = await _backfill.ApplyAsync(Report(("town_1", new[] { "hub" })));

        Assert.Equal(0, result.Updated);
        Assert.Equal("world", (await _context.Domains.FindAsync(1))!.WorldName);
        Assert.Empty(await _backfill.ListMissingAsync());
    }

    [Fact]
    public async Task ARegionLookupWithAWorldPicksThatWorldsDomain()
    {
        _context.Towns.Add(new Town { Id = 1, Name = "Gameplay", Description = "", WgRegionId = "town_1", WorldName = "gameplay" });
        _context.Towns.Add(new Town { Id = 2, Name = "Hub", Description = "", WgRegionId = "TOWN_1", WorldName = "hub" });
        _context.Towns.Add(new Town { Id = 3, Name = "Legacy", Description = "", WgRegionId = "old_1" });
        await _context.SaveChangesAsync();
        var repo = new DomainRepository(_context);

        Assert.Equal(2, (await repo.GetByWgRegionNameAsync("town_1", "Hub"))!.Id);
        Assert.Equal(1, (await repo.GetByWgRegionNameAsync("town_1", "gameplay"))!.Id);
        Assert.Null(await repo.GetByWgRegionNameAsync("town_1", "nether"));
        // Not backfilled yet: still found in any world.
        Assert.Equal(3, (await repo.GetByWgRegionNameAsync("old_1", "hub"))!.Id);
        // World-blind: the lowest id, every time.
        Assert.Equal(1, (await repo.GetByWgRegionNameAsync("town_1"))!.Id);
    }
}
