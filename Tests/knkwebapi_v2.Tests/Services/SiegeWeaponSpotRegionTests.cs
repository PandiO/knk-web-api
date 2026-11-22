using knkwebapi_v2.Enums;
using knkwebapi_v2.Migrations;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#988: a siege weapon spot is its WorldGuard region. The six bounds columns are dropped by a
/// migration; a region drawn through the spot form's region task gets the final name siege_spot_&lt;id&gt; in the
/// scenario's world (the #34/#946 per-entity pattern until #989), a re-run replaces the old region, a region someone else
/// uses is kept, and the temp-region cleanup keeps a spot's region while its rename waits.
/// </summary>
public sealed class SiegeWeaponSpotRegionTests : IDisposable
{
    private readonly KnKDbContext _context;
    private readonly Mock<IRegionService> _regions = new();
    private readonly List<(string Old, string New, string? World, string? Replace)> _renames = new();
    private readonly DomainRegionNameFinalizer _finalizer;
    private bool _pluginRenames = true;

    public SiegeWeaponSpotRegionTests()
    {
        _context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _regions.Setup(r => r.RenameSiegeWeaponSpotRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string?, string?>((o, n, w, x) => _renames.Add((o, n, w, x)))
            .ReturnsAsync(() => _pluginRenames);
        _finalizer = new DomainRegionNameFinalizer(_context, _regions.Object, NullLogger<DomainRegionNameFinalizer>.Instance);
        _context.Towns.Add(new Town { Id = 1, Name = "Cinix", Description = "", WgRegionId = "cinix", WorldName = "siege" });
        _context.SiegeScenarios.Add(new SiegeScenario { Id = 100, Name = "Siege of Cinix", TownId = 1, HubLocationId = 1000 });
        _context.Locations.Add(new Location { Id = 1000, World = "hub_world" });
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    private async Task<SiegeWeaponSpot> AddSpotAsync(int id, string? region)
    {
        var spot = new SiegeWeaponSpot { Id = id, SiegeScenarioId = 100, SiegeTeamId = 202, Name = $"s{id}", WgRegionId = region,
            Facing = SiegeWeaponFacing.North, SizeClass = SiegeWeaponSizeClass.Small };
        _context.SiegeWeaponSpots.Add(spot);
        await _context.SaveChangesAsync();
        return spot;
    }

    private async Task<string?> StoredRegionAsync(int id) =>
        await _context.SiegeWeaponSpots.AsNoTracking().Where(w => w.Id == id).Select(w => w.WgRegionId).SingleAsync();

    [Fact]
    public void Migration_DropsTheSixBoundsColumns_AndTheModelHasNone()
    {
        var builder = new MigrationBuilder("Pomelo.EntityFrameworkCore.MySql");
        var migration = new DropSiegeWeaponSpotBounds();
        typeof(Migration).GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, new object[] { builder });
        var dropped = builder.Operations.OfType<DropColumnOperation>().ToList();
        Assert.Equal(builder.Operations.Count, dropped.Count);
        Assert.All(dropped, d => Assert.Equal("siege_weapon_spots", d.Table));
        Assert.Equal(new[] { "MaxX", "MaxY", "MaxZ", "MinX", "MinY", "MinZ" }, dropped.Select(d => d.Name).OrderBy(n => n));

        var entity = _context.Model.FindEntityType(typeof(SiegeWeaponSpot))!;
        foreach (var gone in new[] { "MinX", "MinY", "MinZ", "MaxX", "MaxY", "MaxZ" }) Assert.Null(entity.FindProperty(gone));
        Assert.NotNull(entity.FindProperty(nameof(SiegeWeaponSpot.WgRegionId)));
    }

    [Fact]
    public async Task DrawnRegion_IsRenamedToSiegeSpotAndId_InTheScenariosWorld()
    {
        var spot = await AddSpotAsync(7, "tempregion_worldtask_41");
        Assert.True(await _finalizer.FinalizeSiegeWeaponSpotAsync(spot, null));
        Assert.Equal(("tempregion_worldtask_41", "siege_spot_7", (string?)"siege", (string?)null), Assert.Single(_renames));
        Assert.Equal("siege_spot_7", await StoredRegionAsync(7));
    }

    [Fact]
    public async Task ReRun_ReplacesTheSpotsOwnFinalRegion()
    {
        var spot = await AddSpotAsync(7, "tempregion_worldtask_42");
        Assert.True(await _finalizer.FinalizeSiegeWeaponSpotAsync(spot, "siege_spot_7"));
        Assert.Equal("siege_spot_7", Assert.Single(_renames).Replace);
    }

    [Fact]
    public async Task APickedRegion_IsLeftAlone()
    {
        var spot = await AddSpotAsync(7, "north_tower");
        Assert.False(await _finalizer.FinalizeSiegeWeaponSpotAsync(spot, null));
        Assert.Empty(_renames);
    }

    [Fact]
    public async Task ATempRegionAnotherSpotStandsOn_KeepsItsName()
    {
        await AddSpotAsync(6, "tempregion_worldtask_41");
        var spot = await AddSpotAsync(7, "tempregion_worldtask_41");
        Assert.False(await _finalizer.FinalizeSiegeWeaponSpotAsync(spot, null));
        Assert.Empty(_renames);
    }

    [Fact]
    public async Task AFailedRename_KeepsTheTempName_AndTheFinalizeAllPassRetriesIt()
    {
        _pluginRenames = false;
        var spot = await AddSpotAsync(7, "tempregion_worldtask_41");
        Assert.False(await _finalizer.FinalizeSiegeWeaponSpotAsync(spot, null));
        Assert.Equal("tempregion_worldtask_41", await StoredRegionAsync(7));

        _pluginRenames = true;
        _context.ChangeTracker.Clear();
        var pass = await _finalizer.FinalizeAllAsync();
        Assert.Equal((1, 1), (pass.Found, pass.Renamed));
        Assert.Equal("siege_spot_7", await StoredRegionAsync(7));
    }

    [Fact]
    public async Task TheTempRegionCleanup_KeepsASpotsRegion()
    {
        await AddSpotAsync(7, "tempregion_worldtask_41");
        var usedBy = typeof(TempRegionReleaseService).GetMethod("UsedByAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var service = new TempRegionReleaseService(_context);
        var reason = await (Task<string?>)usedBy.Invoke(service, new object[] { "TEMPREGION_WORLDTASK_41", CancellationToken.None })!;
        Assert.Equal("a siege weapon spot uses it", reason);
    }
}
