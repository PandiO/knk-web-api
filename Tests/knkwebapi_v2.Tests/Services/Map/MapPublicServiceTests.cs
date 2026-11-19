using System.Text.Json;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Map;
using knkwebapi_v2.Tests.Api;
using knkwebapi_v2.Tests.Services.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Map;

/// <summary>
/// Overview map L2 (PandiO/knk-workspace#736; OVERVIEW_MAP_DESIGN § 5 rules 1-4, § 6.2.1-6.2.3, O18-O20, O32):
/// markers with names, types and pins (A04) and the public panels with exactly the O32 fields (A05).
/// </summary>
public sealed class MapPublicServiceTests : IDisposable
{
    private readonly KnKDbContext _db;
    private readonly ManualClock _clock = new(new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc));
    private readonly MapOptions _options = new() { Enabled = true, Markers = new MapMarkerOptions { CacheSeconds = 0 } };
    private readonly MapMarkerCache _cache = new();

    public MapPublicServiceTests()
    {
        _db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        _db.Locations.Add(new Location { Id = 100, X = 12.5, Y = 70, Z = -3.5, World = "world" });
        _db.Towns.Add(new Town { Id = 1, Name = "Rivia", Description = "Capital", WgRegionId = "Domain_1", WorldName = "world" });
        _db.Towns.Add(new Town { Id = 9, Name = "Nowhere", Description = "", WgRegionId = "domain_9", WorldName = "world" });
        _db.Districts.Add(new District { Id = 2, Name = "Old Quarter", Description = "By the river", WgRegionId = "domain_2", TownId = 1, WorldName = "world" });
        _db.Districts.Add(new District { Id = 3, Name = "Market", Description = "", WgRegionId = "domain_3", TownId = 1, WorldName = "world" });
        _db.Streets.Add(new Street { Id = 50, Name = "Main Street" });
        _db.Structures.Add(new Structure
        {
            Id = 10, Name = "Bakery", Description = "Bread", WgRegionId = "domain_10", WorldName = "world",
            DistrictId = 2, StreetId = 50, HouseNumber = 7, LocationId = 100,
        });
        _db.Structures.Add(new Structure
        {
            Id = 11, Name = "Smithy", Description = "Iron", WgRegionId = "domain_11", WorldName = "world",
            DistrictId = 2, StreetId = 50, HouseNumber = 9,
        });
        _db.GateStructures.Add(new GateStructure
        {
            Id = 12, Name = "North Gate", Description = "Big gate", WgRegionId = "domain_12", WorldName = "world",
            DistrictId = 3, StreetId = 50, HouseNumber = 1, CurrentSiegeId = 500,
            IsDestroyedOverride = null, OpenedStateOverride = null,
        });
        _db.Set<GateDoor>().Add(new GateDoor
        {
            Id = 21, GateStructureId = 12, Name = "West", OpenedState = GateDoorOpenState.OPEN, HealthCurrent = 300, HealthMax = 500,
        });
        _db.Set<GateDoor>().Add(new GateDoor
        {
            Id = 20, GateStructureId = 12, Name = "East", OpenedState = GateDoorOpenState.CLOSED, IsDestroyed = true, HealthCurrent = 0, HealthMax = 500,
        });
        _db.SiegeMatches.Add(new SiegeMatch { Id = 500, Status = SiegeMatchStatus.InProgress });
        _db.DomainSubRegions.Add(new DomainSubRegion { Id = 1, DomainId = 1, WgRegionId = "domain_1_sub_1" });

        _db.MapRegionOutlines.AddRange(
            Outline("domain_1", 1, Square(0, 0, 100)),
            Outline("domain_1_sub_1", 1, Square(200, 0, 10)),
            Outline("domain_1_old", 1, Square(900, 900, 10)), // stale: no longer a sub-region of town 1
            Outline("domain_2", 2, Square(0, 0, 40)),
            // District 3 is an L shape: the centroid is not the bounding box centre.
            Outline("domain_3", 3, "[[0,0],[20,0],[20,10],[10,10],[10,20],[0,20]]"),
            Outline("domain_10", 10, Square(10, 10, 4)),
            Outline("domain_11", 11, Square(20, 20, 4)),
            Outline("domain_12", 12, Square(30, 30, 4)),
            Outline("domain_99", null, Square(5, 5, 5))); // no domain: ignored
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static string Square(int x, int z, int size) =>
        $"[[{x},{z}],[{x + size},{z}],[{x + size},{z + size}],[{x},{z + size}]]";

    private static MapRegionOutline Outline(string region, int? domainId, string points) => new()
    {
        WgRegionId = region, WorldName = "world", PointsJson = points, MinY = 60, MaxY = 120, DomainId = domainId,
        UpdatedAt = DateTime.UtcNow,
    };

    private MapPublicService Service() =>
        new(_db, new TestOptionsMonitor(_options), _cache, NullLogger<MapPublicService>.Instance, _clock);

    private sealed class TestOptionsMonitor : IOptionsMonitor<MapOptions>
    {
        public TestOptionsMonitor(MapOptions value) => CurrentValue = value;
        public MapOptions CurrentValue { get; }
        public MapOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<MapOptions, string?> listener) => null;
    }

    // ===== Markers (A04) =====

    [Fact]
    public async Task Markers_ListTownsDistrictsAndStructures_LabelledWithTheirNames() // A04, O20
    {
        var markers = await Service().GetMarkersAsync();

        Assert.Equal(new[] { "Rivia" }, markers.Towns.Select(t => t.Name)); // Nowhere has no pin
        Assert.Equal(new[] { "Old Quarter", "Market" }, markers.Districts.Select(d => d.Name));
        Assert.Equal(new[] { 1, 1 }, markers.Districts.Select(d => d.TownId));
        Assert.Equal(new[] { "Bakery", "Smithy", "North Gate" }, markers.Structures.Select(s => s.Name));
        Assert.DoesNotContain("domain_", JsonSerializer.Serialize(markers));
    }

    [Fact]
    public async Task Markers_StructuresCarryTypeAndSubtype() // A04, O06
    {
        var markers = await Service().GetMarkersAsync();

        var bakery = markers.Structures.Single(s => s.Id == 10);
        Assert.Equal(("Structure", (string?)null, "Structure"), (bakery.Type, bakery.Subtype, bakery.TypeLabel));
        Assert.Equal((2, 1), (bakery.DistrictId, bakery.TownId));
        var gate = markers.Structures.Single(s => s.Id == 12);
        Assert.Equal(("Structure", "Gate", "Structure, Gate"), (gate.Type, gate.Subtype, gate.TypeLabel));
        Assert.Equal((3, 1), (gate.DistrictId, gate.TownId));
    }

    [Fact]
    public async Task Markers_PinAtTheSpawnLocation_ElseAtTheOutlineCentroid() // A04, O18
    {
        var markers = await Service().GetMarkersAsync();

        var bakery = markers.Structures.Single(s => s.Id == 10).Pin;
        Assert.Equal(("location", "world", 12.5, (double?)70, -3.5), (bakery.Source, bakery.World, bakery.X, bakery.Y, bakery.Z));

        var smithy = markers.Structures.Single(s => s.Id == 11).Pin;
        Assert.Equal(("centroid", 22.5, (double?)null, 22.5), (smithy.Source, smithy.X, smithy.Y, smithy.Z));

        // The L shape's area centroid (about 8.33, 8.33), +0.5 to the block centre; not the box centre 10.
        var market = markers.Districts.Single(d => d.Id == 3).Pin;
        Assert.Equal(("centroid", 8.8, 8.8), (market.Source, market.X, market.Z));
    }

    [Fact]
    public async Task Markers_ShapeIsTheMainRegionThenCurrentSubRegions_StaleAndUnlinkedRowsIgnored()
    {
        var markers = await Service().GetMarkersAsync();

        var town = markers.Towns.Single();
        Assert.Equal(2, town.Outlines.Count);
        Assert.Equal(new[] { 0, 0 }, town.Outlines[0].Points[0]); // main region (WgRegionId "Domain_1", any case) first
        Assert.Equal(new[] { 200, 0 }, town.Outlines[1].Points[0]); // its sub-region
        Assert.Equal((60, 120, "world"), (town.Outlines[0].MinY, town.Outlines[0].MaxY, town.Outlines[0].World));
        Assert.Equal((50.5, 50.5), (town.Pin.X, town.Pin.Z)); // centroid of the main region only
        Assert.DoesNotContain(markers.Towns.Concat<object>(markers.Districts).Concat(markers.Structures),
            m => JsonSerializer.Serialize(m).Contains("900"));
    }

    [Fact]
    public async Task Markers_DomainWithLocationButNoOutline_GetsAPinWithoutOutlines()
    {
        _db.Locations.Add(new Location { Id = 101, X = 1, Y = 2, Z = 3, World = "hub" });
        _db.Towns.Add(new Town { Id = 30, Name = "Hub", Description = "", WgRegionId = "domain_30", LocationId = 101 });
        await _db.SaveChangesAsync();

        var hub = (await Service().GetMarkersAsync()).Towns.Single(t => t.Id == 30);

        Assert.Equal(("location", "hub"), (hub.Pin.Source, hub.Pin.World));
        Assert.Empty(hub.Outlines);
    }

    [Fact]
    public async Task Markers_VersionChangesWhenAnOutlineChanges()
    {
        var before = (await Service().GetMarkersAsync()).Version;
        Assert.Equal(before, (await Service().GetMarkersAsync()).Version);

        var row = _db.MapRegionOutlines.Single(o => o.WgRegionId == "domain_11");
        row.PointsJson = Square(20, 20, 8);
        await _db.SaveChangesAsync();

        Assert.NotEqual(before, (await Service().GetMarkersAsync()).Version);
    }

    [Fact]
    public async Task Markers_AreReusedForCacheSeconds_ThenRebuilt()
    {
        _options.Markers.CacheSeconds = 30;
        var first = await Service().GetMarkersAsync();
        _db.Towns.Single(t => t.Id == 1).Name = "Rivia Renamed";
        await _db.SaveChangesAsync();

        Assert.Same(first, await Service().GetMarkersAsync());

        _clock.Advance(TimeSpan.FromSeconds(31));
        var rebuilt = await Service().GetMarkersAsync();
        Assert.Equal("Rivia Renamed", rebuilt.Towns.Single().Name);
        Assert.NotEqual(first.Version, rebuilt.Version);
    }

    [Fact]
    public async Task Markers_SerializedJson_HasOnlyThePinnedFields() // A05 for markers
    {
        var json = JsonDocument.Parse(JsonSerializer.Serialize(await Service().GetMarkersAsync())).RootElement;

        AssertKeys(json, typeof(MapMarkersDto));
        AssertKeys(json.GetProperty("towns")[0], typeof(MapTownMarkerDto));
        AssertKeys(json.GetProperty("towns")[0].GetProperty("pin"), typeof(MapPinDto));
        AssertKeys(json.GetProperty("towns")[0].GetProperty("outlines")[0], typeof(MapOutlineDto));
        AssertKeys(json.GetProperty("districts")[0], typeof(MapDistrictMarkerDto));
        AssertKeys(json.GetProperty("structures")[0], typeof(MapStructureMarkerDto));
    }

    // ===== Config (O19, § 6.2.1) =====

    [Fact]
    public async Task Config_LabelDistancesFromSettings_ZeroMeansEveryZoom_LaterPartsOff()
    {
        _options.Labels.District.MaxDistance = 1200;
        var config = await Service().GetConfigAsync();

        Assert.True(config.Enabled);
        Assert.Equal(((double?)null, (double?)1200, (double?)600, (double?)1500),
            (config.Labels.Town, config.Labels.District, config.Labels.Structure, config.Labels.Arena));
        Assert.Equal((3, 400), (config.Flash.Count, config.Flash.DurationMs));
        Assert.False(config.LivePlayers.Enabled);
        Assert.False(config.GateCommands.Enabled);
        Assert.Equal((await Service().GetMarkersAsync()).Version, config.MarkerVersion);
        Assert.Matches("^[0-9a-f]{16}$", config.MarkerVersion);
    }

    // Phase 6a (#741, O27): config.gateCommands follows Map:GateCommands:Enabled (L8's buttons read it).
    [Fact]
    public async Task Config_GateCommands_FollowsItsFlag()
    {
        _options.GateCommands.Enabled = true;
        Assert.True((await Service().GetConfigAsync()).GateCommands.Enabled);
    }

    // ===== Panels (A05, O32) =====

    [Fact]
    public async Task TownPanel_NameDescriptionAndDistrictCount_Only()
    {
        var town = await Service().GetTownAsync(1);

        Assert.NotNull(town);
        Assert.Equal((1, "Rivia", "Capital", 2), (town!.Id, town.Name, town.Description, town.DistrictCount));
        AssertKeys(JsonDocument.Parse(JsonSerializer.Serialize(town)).RootElement, typeof(MapTownPanelDto));
    }

    [Fact]
    public async Task DistrictPanel_NameDescriptionAndTown_Only()
    {
        var district = await Service().GetDistrictAsync(2);

        Assert.Equal(("Old Quarter", "By the river", "Rivia"), (district!.Name, district.Description, district.TownName));
        AssertKeys(JsonDocument.Parse(JsonSerializer.Serialize(district)).RootElement, typeof(MapDistrictPanelDto));
    }

    [Fact]
    public async Task StructurePanel_PlainStructure_HasNoGate()
    {
        var s = await Service().GetStructureAsync(10);

        Assert.Equal(("Bakery", "Bread", "Structure", (string?)null), (s!.Name, s.Description, s.Type, s.Subtype));
        Assert.Equal(("Old Quarter", "Rivia", "Main Street", 7), (s.DistrictName, s.TownName, s.StreetName, s.HouseNumber));
        Assert.Null(s.Gate);
        AssertKeys(JsonDocument.Parse(JsonSerializer.Serialize(s)).RootElement, typeof(MapStructurePanelDto));
    }

    [Fact]
    public async Task StructurePanel_Gate_AddsDoorsWithStateDestroyedHealth_AndUnderSiege()
    {
        var s = await Service().GetStructureAsync(12);

        Assert.Equal("Gate", s!.Subtype);
        Assert.True(s.Gate!.UnderSiege);
        Assert.Equal(new[] { "East", "West" }, s.Gate.Doors.Select(d => d.Name));
        var east = s.Gate.Doors[0];
        Assert.Equal(("closed", true, 0.0, 500.0), (east.OpenState, east.IsDestroyed, east.HealthCurrent, east.HealthMax));
        var west = s.Gate.Doors[1];
        Assert.Equal(("open", false, 300.0), (west.OpenState, west.IsDestroyed, west.HealthCurrent));

        var json = JsonDocument.Parse(JsonSerializer.Serialize(s)).RootElement;
        AssertKeys(json, typeof(MapStructurePanelDto));
        AssertKeys(json.GetProperty("gate"), typeof(MapGatePanelDto));
        AssertKeys(json.GetProperty("gate").GetProperty("doors")[0], typeof(MapGateDoorPanelDto));
    }

    [Fact]
    public async Task StructurePanel_Gate_StructureOverridesWin_AndAFinishedMatchIsNoSiege()
    {
        var gate = _db.GateStructures.Single(g => g.Id == 12);
        gate.OpenedStateOverride = GateDoorOpenState.JAMMED;
        gate.IsDestroyedOverride = false;
        _db.SiegeMatches.Single().Status = SiegeMatchStatus.Completed;
        await _db.SaveChangesAsync();

        var s = await Service().GetStructureAsync(12);

        Assert.False(s!.Gate!.UnderSiege);
        Assert.All(s.Gate.Doors, d => Assert.Equal(("jammed", false), (d.OpenState, d.IsDestroyed)));
    }

    [Fact]
    public async Task Panels_UnknownOrWrongTypeIds_AreNull()
    {
        var service = Service();

        Assert.Null(await service.GetTownAsync(2)); // a district
        Assert.Null(await service.GetDistrictAsync(1)); // a town
        Assert.Null(await service.GetStructureAsync(1)); // a town
        Assert.Null(await service.GetStructureAsync(404));
    }

    // ===== Geometry (O18) =====

    [Fact]
    public void Centroid_OfASquare_IsItsCentre_DegenerateFallsBackToTheMean()
    {
        Assert.Equal((5.0, 5.0), MapGeometry.Centroid(new[] { (0, 0), (10, 0), (10, 10), (0, 10) }));
        Assert.Equal((5.0, 0.0), MapGeometry.Centroid(new[] { (0, 0), (10, 0) }));
        Assert.Null(MapGeometry.Centroid(Array.Empty<(int, int)>()));
    }

    private static void AssertKeys(JsonElement element, Type dto)
    {
        var keys = element.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(MapPublicDtoFieldTests.Pinned[dto].OrderBy(n => n, StringComparer.Ordinal).ToArray(), keys);
    }
}
