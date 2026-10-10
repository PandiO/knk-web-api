using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-43: a domain's temporary world-task region (tempregion_worldtask_&lt;n&gt;) is renamed to domain_&lt;id&gt; for every
/// domain type, the plugin is told the type and the parent's region, and the new name is stored on the domain. The
/// finalize-all pass renames existing temp-named domains parents first.
/// </summary>
public class DomainRegionNameFinalizerTests : IDisposable
{
    private readonly KnKDbContext _context;
    private readonly Mock<IRegionService> _regions = new();
    private readonly List<(string Old, string New, string? Type, string? Parent)> _renames = new();
    private readonly List<string?> _renameWorlds = new();
    private readonly DomainRegionNameFinalizer _finalizer;

    public DomainRegionNameFinalizerTests()
    {
        _context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _regions.Setup(r => r.RenameRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string?, string?, string?>((o, n, t, p, w) => { _renames.Add((o, n, t, p)); _renameWorlds.Add(w); })
            .ReturnsAsync(true);
        _finalizer = new DomainRegionNameFinalizer(_context, _regions.Object, NullLogger<DomainRegionNameFinalizer>.Instance);
    }

    public void Dispose() => _context.Dispose();

    private async Task SeedAsync(string townRegion = "town_1", string districtRegion = "domain_10", string gateRegion = "tempregion_worldtask_97")
    {
        _context.Towns.Add(new Town { Id = 1, Name = "Cinix", Description = "t", WgRegionId = townRegion });
        _context.Districts.Add(new District { Id = 10, Name = "Old Town", Description = "d", WgRegionId = districtRegion, TownId = 1 });
        _context.GateStructures.Add(new GateStructure { Id = 11, Name = "Keep Gate", Description = "g", WgRegionId = gateRegion, DistrictId = 10, StreetId = 1 });
        await _context.SaveChangesAsync();
    }

    private async Task<string> StoredRegionIdAsync(int id) =>
        await _context.Domains.AsNoTracking().Where(d => d.Id == id).Select(d => d.WgRegionId).SingleAsync();

    [Fact]
    public async Task Gate_IsRenamedAsGateUnderItsDistrict_AndStored()
    {
        await SeedAsync();
        var gate = await _context.GateStructures.SingleAsync(g => g.Id == 11);

        Assert.True(await _finalizer.FinalizeAsync(gate));

        Assert.Equal(("tempregion_worldtask_97", "domain_11", "GateStructure", "domain_10"), Assert.Single(_renames));
        Assert.Equal("domain_11", gate.WgRegionId);
        Assert.Equal("domain_11", await StoredRegionIdAsync(11));
    }

    [Fact]
    public async Task TownAndPlainStructure_PassTheirOwnTypes()
    {
        _context.Towns.Add(new Town { Id = 1, Name = "Cinix", Description = "t", WgRegionId = "tempregion_worldtask_1" });
        _context.Districts.Add(new District { Id = 10, Name = "Old Town", Description = "d", WgRegionId = "domain_10", TownId = 1 });
        _context.Structures.Add(new Structure { Id = 12, Name = "House", Description = "h", WgRegionId = "tempregion_worldtask_2", DistrictId = 10, StreetId = 1, HouseNumber = 1 });
        await _context.SaveChangesAsync();

        await _finalizer.FinalizeAsync(await _context.Towns.SingleAsync());
        await _finalizer.FinalizeAsync(await _context.Structures.SingleAsync(s => s.Id == 12));

        Assert.Equal(new[]
        {
            ("tempregion_worldtask_1", "domain_1", (string?)"Town", (string?)null),
            ("tempregion_worldtask_2", "domain_12", "Structure", "domain_10")
        }, _renames);
    }

    [Fact]
    public async Task TheRenameNamesTheDomainsWorld()
    {
        // KNG-111: the region id is only unique within a world, so the plugin is told which world's region to rename.
        _context.Towns.Add(new Town { Id = 1, Name = "Hubtown", Description = "t", WgRegionId = "tempregion_worldtask_5", WorldName = "hub" });
        await _context.SaveChangesAsync();

        await _finalizer.FinalizeAsync(await _context.Towns.SingleAsync());

        Assert.Equal("hub", Assert.Single(_renameWorlds));
    }

    [Fact]
    public async Task FinalName_IsLeftAlone()
    {
        await SeedAsync(gateRegion: "domain_11");

        Assert.False(await _finalizer.FinalizeAsync(await _context.GateStructures.SingleAsync()));
        Assert.Empty(_renames);
    }

    [Fact]
    public async Task FailedRename_KeepsTheTemporaryName()
    {
        await SeedAsync();
        _regions.Setup(r => r.RenameRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(false);

        Assert.False(await _finalizer.FinalizeAsync(await _context.GateStructures.SingleAsync()));
        Assert.Equal("tempregion_worldtask_97", await StoredRegionIdAsync(11));
    }

    [Fact]
    public async Task UnreachablePlugin_DoesNotThrow()
    {
        await SeedAsync();
        _regions.Setup(r => r.RenameRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        Assert.False(await _finalizer.FinalizeAsync(await _context.GateStructures.SingleAsync()));
        Assert.Equal("tempregion_worldtask_97", await StoredRegionIdAsync(11));
    }

    [Fact]
    public async Task FinalizeAll_RenamesParentsFirst_SoChildrenGetTheParentsFinalName()
    {
        await SeedAsync(townRegion: "tempregion_worldtask_1", districtRegion: "tempregion_worldtask_5");
        _context.ChangeTracker.Clear();

        var result = await _finalizer.FinalizeAllAsync();

        Assert.Equal((3, 3), (result.Found, result.Renamed));
        Assert.Empty(result.Failed);
        Assert.Equal(new[]
        {
            ("tempregion_worldtask_1", "domain_1", (string?)"Town", (string?)null),
            ("tempregion_worldtask_5", "domain_10", "District", "domain_1"),
            ("tempregion_worldtask_97", "domain_11", "GateStructure", "domain_10")
        }, _renames);
        Assert.Equal(new[] { "domain_1", "domain_10", "domain_11" },
            new[] { await StoredRegionIdAsync(1), await StoredRegionIdAsync(10), await StoredRegionIdAsync(11) });
    }

    [Fact]
    public async Task FinalizeAll_ReportsFailures_AndASecondRunRetriesOnlyThose()
    {
        await SeedAsync(districtRegion: "tempregion_worldtask_5");
        _regions.Setup(r => r.RenameRegionAsync("tempregion_worldtask_97", It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(false);

        var first = await _finalizer.FinalizeAllAsync();
        Assert.Equal((2, 1), (first.Found, first.Renamed));
        Assert.Equal("GateStructure 11 (tempregion_worldtask_97)", Assert.Single(first.Failed));

        var second = await _finalizer.FinalizeAllAsync();
        Assert.Equal(1, second.Found);
    }
}
