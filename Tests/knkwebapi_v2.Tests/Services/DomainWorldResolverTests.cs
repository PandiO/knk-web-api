using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-111: a domain's world comes from the form, the region world task, its Location or its parent; every source
/// that gives one must agree, children stay in their parent's world, and a region id is unique per world (so the
/// same id in two worlds is fine).
/// </summary>
public class DomainWorldResolverTests : IDisposable
{
    private readonly KnKDbContext _context;
    private readonly DomainWorldResolver _resolver;

    public DomainWorldResolverTests()
    {
        _context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _resolver = new DomainWorldResolver(_context);
    }

    public void Dispose() => _context.Dispose();

    private async Task SeedHierarchyAsync()
    {
        _context.Towns.Add(new Town { Id = 1, Name = "Hubtown", Description = "", WgRegionId = "domain_1", WorldName = "hub" });
        _context.Districts.Add(new District { Id = 2, Name = "Plaza", Description = "", WgRegionId = "domain_2", TownId = 1, WorldName = "hub" });
        _context.Locations.Add(new Location { Id = 50, Name = "spawn", World = "gameplay" });
        _context.WorldTasks.Add(new WorldTask
        {
            Id = 42,
            TaskType = "WgRegionId",
            Status = "Completed",
            OutputJson = "{\"fieldName\":\"WgRegionId\",\"regionId\":\"tempregion_worldtask_42\",\"worldName\":\"hub\"}"
        });
        await _context.SaveChangesAsync();
    }

    [Fact]
    public async Task TheRegionWorldTaskGivesTheWorld()
    {
        await SeedHierarchyAsync();

        var result = await _resolver.TryResolveAsync(new DomainWorldRequest { WgRegionId = "tempregion_worldtask_42" });

        Assert.Equal("hub", result.WorldName);
        Assert.Equal("region world task", result.Source);
    }

    [Fact]
    public async Task ASavedLocationGivesTheWorld()
    {
        await SeedHierarchyAsync();

        var result = await _resolver.TryResolveAsync(new DomainWorldRequest { WgRegionId = "town_x", LocationId = 50 });

        Assert.Equal("gameplay", result.WorldName);
    }

    [Fact]
    public async Task AChildWithoutOtherSourcesTakesItsParentsWorld()
    {
        await SeedHierarchyAsync();

        var result = await _resolver.TryResolveAsync(new DomainWorldRequest { WgRegionId = "keep", ParentDomainId = 2 });

        Assert.Equal("hub", result.WorldName);
        Assert.Equal("parent domain", result.Source);
    }

    [Fact]
    public async Task AChildCannotBeInAnotherWorldThanItsParent()
    {
        await SeedHierarchyAsync();

        var result = await _resolver.TryResolveAsync(new DomainWorldRequest
        {
            WgRegionId = "keep", LocationId = 50, ParentDomainId = 2
        });

        Assert.Null(result.WorldName);
        Assert.Equal(DomainWorldException.WorldConflict, result.ErrorCode);
        Assert.Contains("gameplay", result.Error);
        Assert.Contains("hub", result.Error);
    }

    [Fact]
    public async Task WorldsAreComparedIgnoringCase()
    {
        await SeedHierarchyAsync();

        var result = await _resolver.TryResolveAsync(new DomainWorldRequest
        {
            RequestedWorld = "HUB", WgRegionId = "tempregion_worldtask_42", ParentDomainId = 2
        });

        Assert.Equal("HUB", result.WorldName);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task WithoutAnySourceTheFormMustAskAndSavingIsRefused()
    {
        var result = await _resolver.TryResolveAsync(new DomainWorldRequest { WgRegionId = "town_without_task" });

        Assert.True(result.NeedsWorld);
        var ex = await Assert.ThrowsAsync<DomainWorldException>(() =>
            _resolver.ResolveAsync(new DomainWorldRequest { WgRegionId = "town_without_task" }));
        Assert.Equal(DomainWorldException.WorldRequired, ex.Code);
    }

    [Fact]
    public async Task TheSameRegionIdIsAllowedInAnotherWorldButNotTwiceInOneWorld()
    {
        _context.Towns.Add(new Town { Id = 1, Name = "Gameplay town", Description = "", WgRegionId = "town_1", WorldName = "gameplay" });
        await _context.SaveChangesAsync();

        var otherWorld = await _resolver.TryResolveAsync(new DomainWorldRequest { RequestedWorld = "hub", WgRegionId = "town_1" });
        var sameWorld = await _resolver.TryResolveAsync(new DomainWorldRequest { RequestedWorld = "Gameplay", WgRegionId = "TOWN_1" });

        Assert.Equal("hub", otherWorld.WorldName);
        Assert.Equal(DomainWorldException.RegionTaken, sameWorld.ErrorCode);
        Assert.Contains("Gameplay town", sameWorld.Error);
    }

    [Fact]
    public async Task AnUpdateKeepsTheSavedWorldWhenNothingElseGivesOne()
    {
        _context.Towns.Add(new Town { Id = 1, Name = "Old", Description = "", WgRegionId = "domain_1", WorldName = "world" });
        await _context.SaveChangesAsync();

        var result = await _resolver.TryResolveAsync(new DomainWorldRequest { DomainId = 1, WgRegionId = "domain_1" });

        Assert.Equal("world", result.WorldName);
        Assert.Equal("saved domain", result.Source);
    }

    [Fact]
    public async Task AnUpdateDoesNotTripOverAnExistingDuplicateItDidNotTouch()
    {
        // Two rows that already share a region in one world (data from before KNG-111): editing one keeps working.
        _context.Towns.Add(new Town { Id = 1, Name = "A", Description = "", WgRegionId = "dup", WorldName = "world" });
        _context.Towns.Add(new Town { Id = 2, Name = "B", Description = "", WgRegionId = "dup", WorldName = "world" });
        await _context.SaveChangesAsync();

        var result = await _resolver.TryResolveAsync(new DomainWorldRequest { DomainId = 2, WgRegionId = "dup" });

        Assert.Equal("world", result.WorldName);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ATownCannotMoveAwayFromItsDistricts()
    {
        await SeedHierarchyAsync();

        var result = await _resolver.TryResolveAsync(new DomainWorldRequest
        {
            DomainId = 1, RequestedWorld = "gameplay", WgRegionId = "domain_1"
        });

        Assert.Equal(DomainWorldException.WorldConflict, result.ErrorCode);
        Assert.Contains("Plaza", result.Error);
    }

    [Fact]
    public async Task AnEmbeddedLocationWorldCountsBeforeItIsSaved()
    {
        var result = await _resolver.TryResolveAsync(new DomainWorldRequest { WgRegionId = "t", LocationWorld = " hub " });

        Assert.Equal("hub", result.WorldName);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("not json", null)]
    [InlineData("{\"regionId\":\"x\"}", null)]
    [InlineData("{\"WorldName\":\"hub\"}", "hub")]
    [InlineData("{\"worldName\":\"  \"}", null)]
    public void WorldNameIsReadFromATaskOutput(string? json, string? expected)
    {
        Assert.Equal(expected, DomainWorldResolver.WorldNameFromTaskOutput(json));
    }

    [Fact]
    public async Task ARequestedWorldLongerThanTheColumnIsRefused()
    {
        var result = await _resolver.TryResolveAsync(new DomainWorldRequest { RequestedWorld = new string('w', 65) });

        Assert.Equal(DomainWorldException.WorldConflict, result.ErrorCode);
    }
}
