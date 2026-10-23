using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-254: a domain's sub-regions. Each lives in its domain's world, its region is used by no other domain or
/// sub-region there, and saving renames it to domain_&lt;domainId&gt;_sub_&lt;id&gt; as a copy of the main region (a
/// re-run of its world task replaces the old region, like KNG-103). Lookups by a sub-region answer its domain.
/// </summary>
public class DomainSubRegionTests : IDisposable
{
    private readonly KnKDbContext _context;
    private readonly Mock<IRegionService> _regions = new();
    private readonly List<(string Old, string New, string? Type, string MainRegion, string? World, string? Replace)> _subRenames = new();
    private readonly DomainRegionNameFinalizer _finalizer;
    private readonly DomainSubRegionService _service;

    public DomainSubRegionTests()
    {
        _context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _regions.Setup(r => r.RenameSubRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string?, string, string?, string?>((o, n, t, m, w, x) => _subRenames.Add((o, n, t, m, w, x)))
            .ReturnsAsync(true);
        _regions.Setup(r => r.RenameRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);
        _finalizer = new DomainRegionNameFinalizer(_context, _regions.Object, NullLogger<DomainRegionNameFinalizer>.Instance);
        _service = new DomainSubRegionService(_context, _finalizer, NullLogger<DomainSubRegionService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    private async Task SeedAsync()
    {
        _context.Towns.Add(new Town { Id = 1, Name = "Cinix", Description = "t", WgRegionId = "domain_1", WorldName = "world" });
        _context.Districts.Add(new District { Id = 10, Name = "Old Town", Description = "d", WgRegionId = "domain_10", TownId = 1, WorldName = "world" });
        _context.GateStructures.Add(new GateStructure { Id = 11, Name = "Garrison", Description = "g", WgRegionId = "domain_11", DistrictId = 10, StreetId = 1, WorldName = "world" });
        _context.WorldTasks.Add(TaskDrawnIn(301, "world"));
        _context.WorldTasks.Add(TaskDrawnIn(302, "world"));
        _context.WorldTasks.Add(TaskDrawnIn(303, "nether"));
        await _context.SaveChangesAsync();
    }

    private static WorldTask TaskDrawnIn(int id, string world) => new()
    {
        Id = id,
        TaskType = "ReagionCreate",
        Status = "Completed",
        OutputJson = $"{{\"fieldName\":\"WgRegionId\",\"regionId\":\"tempregion_worldtask_{id}\",\"worldName\":\"{world}\"}}"
    };

    private Task<DomainSubRegion> StoredAsync(int id) => _context.DomainSubRegions.AsNoTracking().SingleAsync(s => s.Id == id);

    [Fact]
    public async Task Create_RenamesTheDrawnRegionAsACopyOfTheMainRegion()
    {
        await SeedAsync();

        var created = await _service.CreateAsync(new DomainSubRegionDto { DomainId = 11, WgRegionId = "tempregion_worldtask_301", Name = " Tower " });

        string expected = $"domain_11_sub_{created.Id}";
        Assert.Equal(("tempregion_worldtask_301", expected, (string?)"GateStructure", "domain_11", (string?)"world", (string?)null), Assert.Single(_subRenames));
        Assert.Equal(expected, created.WgRegionId);
        Assert.Equal("Tower", created.Name);
        Assert.Equal(expected, (await StoredAsync(created.Id!.Value)).WgRegionId);
    }

    [Fact]
    public async Task Create_AdoptsAnExistingRegion_AndRenamesIt()
    {
        await SeedAsync();

        var created = await _service.CreateAsync(new DomainSubRegionDto { DomainId = 1, WgRegionId = "town_1,2" });

        var rename = Assert.Single(_subRenames);
        Assert.Equal(("town_1,2", $"domain_1_sub_{created.Id}", (string?)"Town", (string?)null), (rename.Old, rename.New, rename.Type, rename.Replace));
    }

    [Fact]
    public async Task Create_WhenThePluginRefuses_KeepsTheDrawnName()
    {
        await SeedAsync();
        _regions.Setup(r => r.RenameSubRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(false);

        var created = await _service.CreateAsync(new DomainSubRegionDto { DomainId = 11, WgRegionId = "tempregion_worldtask_301" });

        Assert.Equal("tempregion_worldtask_301", (await StoredAsync(created.Id!.Value)).WgRegionId);
    }

    [Theory]
    [InlineData("domain_11")]   // the domain's own main region
    [InlineData("domain_10")]   // another domain's main region
    [InlineData("DOMAIN_1")]    // case-insensitive, like WorldGuard
    public async Task Create_RefusesARegionADomainUses(string regionId)
    {
        await SeedAsync();

        var ex = await Assert.ThrowsAsync<DomainWorldException>(() =>
            _service.CreateAsync(new DomainSubRegionDto { DomainId = 11, WgRegionId = regionId }));

        Assert.Equal(DomainWorldException.RegionTaken, ex.Code);
        Assert.Empty(_subRenames);
    }

    [Fact]
    public async Task Create_RefusesAnotherDomainsSubRegion()
    {
        await SeedAsync();
        _context.DomainSubRegions.Add(new DomainSubRegion { Id = 5, DomainId = 10, WgRegionId = "domain_10_sub_5" });
        await _context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DomainWorldException>(() =>
            _service.CreateAsync(new DomainSubRegionDto { DomainId = 11, WgRegionId = "domain_10_sub_5" }));

        Assert.Equal(DomainWorldException.RegionTaken, ex.Code);
        Assert.Contains("sub-region #5 of Old Town", ex.Message);
    }

    [Fact]
    public async Task Create_RefusesARegionDrawnInAnotherWorld()
    {
        await SeedAsync();

        var ex = await Assert.ThrowsAsync<DomainWorldException>(() =>
            _service.CreateAsync(new DomainSubRegionDto { DomainId = 11, WgRegionId = "tempregion_worldtask_303" }));

        Assert.Equal(DomainWorldException.WorldConflict, ex.Code);
        Assert.Empty(await _context.DomainSubRegions.ToListAsync());
    }

    [Fact]
    public async Task Create_NeedsAnExistingDomainWithAMainRegion()
    {
        await SeedAsync();
        _context.Structures.Add(new Structure { Id = 12, Name = "Unplaced", Description = "s", WgRegionId = "", DistrictId = 10, StreetId = 1, HouseNumber = 1 });
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.CreateAsync(new DomainSubRegionDto { DomainId = 99, WgRegionId = "tempregion_worldtask_301" }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(new DomainSubRegionDto { DomainId = 12, WgRegionId = "tempregion_worldtask_301" }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(new DomainSubRegionDto { DomainId = 11, WgRegionId = "  " }));
    }

    [Fact]
    public async Task Update_AReRunReplacesTheOldRegionUnderTheFinalName()
    {
        await SeedAsync();
        var created = await _service.CreateAsync(new DomainSubRegionDto { DomainId = 11, WgRegionId = "tempregion_worldtask_301" });
        _subRenames.Clear();

        var updated = await _service.UpdateAsync(created.Id!.Value, new DomainSubRegionDto { DomainId = 11, WgRegionId = "tempregion_worldtask_302", Name = "Tower" });

        string finalName = $"domain_11_sub_{created.Id}";
        Assert.Equal(("tempregion_worldtask_302", finalName, (string?)finalName), Pick(Assert.Single(_subRenames)));
        Assert.Equal(finalName, updated.WgRegionId);
    }

    [Fact]
    public async Task Update_OnlyTheName_RenamesNothing()
    {
        await SeedAsync();
        var created = await _service.CreateAsync(new DomainSubRegionDto { DomainId = 11, WgRegionId = "tempregion_worldtask_301" });
        _subRenames.Clear();

        var updated = await _service.UpdateAsync(created.Id!.Value, new DomainSubRegionDto { DomainId = 11, WgRegionId = created.WgRegionId, Name = "Keep" });

        Assert.Empty(_subRenames);
        Assert.Equal("Keep", updated.Name);
    }

    [Fact]
    public async Task Update_CantMoveASubRegionToAnotherDomain()
    {
        await SeedAsync();
        var created = await _service.CreateAsync(new DomainSubRegionDto { DomainId = 11, WgRegionId = "tempregion_worldtask_301" });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateAsync(created.Id!.Value, new DomainSubRegionDto { DomainId = 10, WgRegionId = created.WgRegionId }));
    }

    [Fact]
    public async Task Delete_RemovesTheRow_AndDeletingTheDomainRemovesItsSubRegions()
    {
        await SeedAsync();
        var first = await _service.CreateAsync(new DomainSubRegionDto { DomainId = 11, WgRegionId = "tempregion_worldtask_301" });
        var second = await _service.CreateAsync(new DomainSubRegionDto { DomainId = 11, WgRegionId = "tempregion_worldtask_302" });

        await _service.DeleteAsync(first.Id!.Value);
        Assert.Equal(new int?[] { second.Id }, (await _service.GetByDomainAsync(11)).Select(s => s.Id));

        _context.Domains.Remove(await _context.Domains.SingleAsync(d => d.Id == 11));
        await _context.SaveChangesAsync();
        Assert.Empty(await _context.DomainSubRegions.ToListAsync());
    }

    [Fact]
    public async Task FinalizeAll_RenamesSubRegionsLeftOnATempName_AfterTheDomains()
    {
        await SeedAsync();
        _context.DomainSubRegions.Add(new DomainSubRegion { Id = 7, DomainId = 10, WgRegionId = "tempregion_worldtask_77" });
        _context.DomainSubRegions.Add(new DomainSubRegion { Id = 8, DomainId = 10, WgRegionId = "domain_10_sub_8" });
        await _context.SaveChangesAsync();

        var result = await _finalizer.FinalizeAllAsync();

        Assert.Equal((1, 1), (result.Found, result.Renamed));
        Assert.Equal(("tempregion_worldtask_77", "domain_10_sub_7", (string?)null), Pick(Assert.Single(_subRenames)));
    }

    [Fact]
    public async Task TheMainRegionCantBeAnotherDomainsSubRegion()
    {
        await SeedAsync();
        _context.DomainSubRegions.Add(new DomainSubRegion { Id = 5, DomainId = 10, WgRegionId = "domain_10_sub_5" });
        await _context.SaveChangesAsync();

        var result = await new DomainWorldResolver(_context).TryResolveAsync(new DomainWorldRequest
        {
            DomainId = 11,
            WgRegionId = "domain_10_sub_5",
            RequestedWorld = "world"
        });

        Assert.Equal(DomainWorldException.RegionTaken, result.ErrorCode);
    }

    [Fact]
    public async Task ADomainWithSubRegionsCantMoveToAnotherWorld()
    {
        await SeedAsync();
        _context.DomainSubRegions.Add(new DomainSubRegion { Id = 5, DomainId = 11, WgRegionId = "domain_11_sub_5" });
        await _context.SaveChangesAsync();

        var result = await new DomainWorldResolver(_context).TryResolveAsync(new DomainWorldRequest
        {
            DomainId = 11,
            WgRegionId = "domain_11",
            RequestedWorld = "nether"
        });

        Assert.Equal(DomainWorldException.WorldConflict, result.ErrorCode);
        Assert.Contains("sub-regions", result.Error);
    }

    [Fact]
    public async Task TheRepositoryFindsADomainByItsSubRegion_InItsWorld()
    {
        await SeedAsync();
        _context.DomainSubRegions.Add(new DomainSubRegion { Id = 5, DomainId = 11, WgRegionId = "domain_11_sub_5" });
        await _context.SaveChangesAsync();
        var repo = new DomainRepository(_context);

        var match = await repo.GetByAnyRegionAsync("DOMAIN_11_SUB_5", "world");
        var main = await repo.GetByAnyRegionAsync("domain_11", "world");

        Assert.Equal((11, (int?)5), (match!.Domain.Id, match.MatchedSubRegionId));
        Assert.Equal("domain_11_sub_5", Assert.Single(match.Domain.SubRegions).WgRegionId);
        Assert.Equal((11, (int?)null), (main!.Domain.Id, main.MatchedSubRegionId));
        Assert.Null(await repo.GetByAnyRegionAsync("domain_11_sub_5", "nether"));
        Assert.Equal(11, (await repo.GetByWgRegionNameAsync("domain_11_sub_5"))!.Id);
        Assert.Equal(new[] { "domain_11_sub_5" }, (await repo.GetSubRegionIdsByDomainAsync())[11]);
    }

    private static (string Old, string New, string? Replace) Pick((string Old, string New, string? Type, string MainRegion, string? World, string? Replace) r) =>
        (r.Old, r.New, r.Replace);
}
