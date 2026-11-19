using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static knkwebapi_v2.Tests.Services.Territory.TerritoryTestDb;

namespace knkwebapi_v2.Tests.Services.Territory;

/// <summary>
/// Territory V1, the game server contract (PandiO/knk-workspace#326): the inputs the chunk cache is built from, and the
/// report that turns equal-rank overlaps into conflicts and flags structures (A05, A07 data side, G16).
/// </summary>
public class TerritoryServiceTests : IDisposable
{
    private readonly KnKDbContext _db;
    private readonly TerritoryService _service;

    public TerritoryServiceTests()
    {
        _db = Create();
        _service = New(_db);
    }

    public void Dispose() => _db.Dispose();

    private static TerritoryService New(KnKDbContext db) =>
        new(db, new ProductionSettingsReader(db), NullLogger<TerritoryService>.Instance);

    private static TerritoryReportOverlapDto Overlap(TerritoryHolderDto a, TerritoryHolderDto b, params int[][] chunks) =>
        new() { HolderA = a, HolderB = b, Chunks = chunks.ToList() };

    private static TerritoryReportDto Report(params TerritoryReportOverlapDto[] overlaps) =>
        new() { World = "world", Overlaps = overlaps.ToList() };

    [Fact]
    public async Task Inputs_HoldEveryRegionWithItsHolderAndComputedRange()
    {
        var inputs = await _service.GetInputsAsync();

        Assert.True(inputs.ProductionEnabled);
        Assert.Equal(100, inputs.Settings.BaseRange);
        var capital = inputs.Districts.Single(d => d.Id == PhoenixCapital);
        var market = inputs.Districts.Single(d => d.Id == PhoenixMarket);
        Assert.Equal(300, capital.Range); // (100 + 50 × 2) × 1.5
        Assert.True(capital.IsCapital);
        Assert.Equal(200, market.Range);
        Assert.Equal(new[] { "domain_21_sub_40" }, market.SubRegionIds);
        Assert.Equal(("Town", Phoenix, 1), (market.Holder.Kind, market.Holder.Id, market.Rank));
        Assert.Equal(120, inputs.Districts.Single(d => d.Id == RivertownCentre).Range);
        Assert.Equal(200, inputs.Towns.Single(t => t.Id == Phoenix).Range);
        Assert.Single(inputs.Kingdoms);
        Assert.Empty(inputs.CrownLands);
        Assert.Empty(inputs.Awards);
    }

    [Fact]
    public async Task Inputs_CrownLand_IsHeldByItsKingdom_WithItsOwnRange_AndRankTwo()
    {
        _db.CrownLands.Add(new CrownLand { Id = 50, Name = "Royal forest", Description = "c", WgRegionId = "domain_50", WorldName = "world", KingdomId = KingdomId, Range = 80 });
        await _db.SaveChangesAsync();

        var crown = Assert.Single((await _service.GetInputsAsync()).CrownLands);

        Assert.Equal(("Kingdom", KingdomId, 2, 80.0), (crown.Holder.Kind, crown.Holder.Id, crown.Rank, crown.Range));
    }

    [Fact]
    public async Task Inputs_Version_ChangesWithAnInput_AndOnlyThen()
    {
        var first = (await _service.GetInputsAsync()).Version;
        Assert.Equal(first, (await _service.GetInputsAsync()).Version);

        var phoenix = await _db.Towns.SingleAsync(t => t.Id == Phoenix);
        phoenix.Power = 20;
        await _db.SaveChangesAsync();

        var after = await _service.GetInputsAsync();
        Assert.NotEqual(first, after.Version);
        Assert.Equal(140, after.Districts.Single(d => d.Id == PhoenixMarket).Range);
    }

    [Fact]
    public async Task Report_WithProductionOff_IsRefused_AndWritesNothing()
    {
        using var db = Create(productionEnabled: false);
        var service = New(db);

        await Assert.ThrowsAsync<ProductionDisabledException>(() =>
            service.ApplyReportAsync(Report(Overlap(Town(Phoenix), Town(Rivertown), new[] { 1, 1 }))));

        Assert.Empty(db.TerritoryConflicts);
        // Admin reads stay available while the flag is off (G16).
        Assert.False((await service.GetInputsAsync()).ProductionEnabled);
    }

    [Fact]
    public async Task Report_EqualRankOverlap_CreatesOneConflict_ThenUpdatesIt()
    {
        var first = await _service.ApplyReportAsync(Report(Overlap(Town(Rivertown), Town(Phoenix), new[] { 4, 2 }, new[] { 5, 2 })));
        var second = await _service.ApplyReportAsync(Report(Overlap(Town(Phoenix), Town(Rivertown), new[] { 4, 2 }, new[] { 5, 2 }, new[] { 6, 2 })));

        Assert.Equal((1, 0), (first.ConflictsCreated, first.ConflictsUpdated));
        Assert.Equal((0, 1), (second.ConflictsCreated, second.ConflictsUpdated));
        var conflict = Assert.Single(_db.TerritoryConflicts.AsNoTracking());
        Assert.Equal((TerritoryHolderKind.Town, Phoenix, TerritoryHolderKind.Town, Rivertown),
            (conflict.HolderAKind, conflict.HolderAId, conflict.HolderBKind, conflict.HolderBId));
        Assert.Equal(3, conflict.ChunkCount);
        Assert.Equal((4, 2, 6, 2), (conflict.MinChunkX, conflict.MinChunkZ, conflict.MaxChunkX, conflict.MaxChunkZ));
        Assert.Equal(TerritoryConflictStatus.Open, conflict.Status);
    }

    [Fact]
    public async Task Report_TownAgainstCrownLand_OrUnknownHolders_IsSkipped()
    {
        var result = await _service.ApplyReportAsync(Report(
            Overlap(Town(Phoenix), KingdomHolder(KingdomId), new[] { 1, 1 }),
            Overlap(Town(Phoenix), Town(999), new[] { 1, 1 }),
            Overlap(Town(Phoenix), Town(Phoenix), new[] { 1, 1 }),
            Overlap(Town(Phoenix), Town(Rivertown))));

        Assert.Equal(4, result.Skipped.Count);
        Assert.Empty(_db.TerritoryConflicts);
    }

    [Fact]
    public async Task Report_AnOverlapThatIsGone_StaysOpen_ButIsMarkedNotOverlapping()
    {
        await _service.ApplyReportAsync(Report(Overlap(Town(Phoenix), Town(Rivertown), new[] { 1, 1 })));

        var result = await _service.ApplyReportAsync(Report());

        Assert.Equal(1, result.ConflictsNoLongerOverlapping);
        var conflict = Assert.Single(_db.TerritoryConflicts.AsNoTracking());
        Assert.Equal(TerritoryConflictStatus.Open, conflict.Status); // admins resolve, never the system (DESIGN § 3)
        Assert.False(conflict.StillOverlapping);
    }

    [Fact]
    public async Task Report_OnlyTouchesItsOwnWorld()
    {
        await _service.ApplyReportAsync(Report(Overlap(Town(Phoenix), Town(Rivertown), new[] { 1, 1 })));

        await _service.ApplyReportAsync(new TerritoryReportDto { World = "world_nether" });

        Assert.True(Assert.Single(_db.TerritoryConflicts.AsNoTracking()).StillOverlapping);
    }

    [Fact]
    public async Task Report_FlagsStructures_ListsContestedOnesOnTheConflict_AndClearsThemLater()
    {
        var report = Report(Overlap(Town(Phoenix), Town(Rivertown), new[] { 1, 1 }));
        report.Structures = new List<TerritoryReportStructureDto>
        {
            new() { StructureId = Bakery, Flag = "Contested", HolderA = Town(Rivertown), HolderB = Town(Phoenix) },
            new() { StructureId = Mill, Flag = "OutsideTerritory" },
        };

        var result = await _service.ApplyReportAsync(report);

        Assert.Equal(2, result.StructuresFlagged);
        var conflict = await _db.TerritoryConflicts.AsNoTracking().Include(c => c.FlaggedStructures).SingleAsync();
        Assert.Equal(new[] { Bakery }, conflict.FlaggedStructures.Select(s => s.Id));
        var mill = await _service.GetStructureTerritoryAsync(Mill);
        Assert.Equal("OutsideTerritory", mill!.TerritoryFlag);
        Assert.NotNull(mill.TerritoryFlaggedAt);

        // The structures keep their town: the district link is untouched (territory DESIGN § 4).
        Assert.Equal(PhoenixMarket, (await _db.Structures.AsNoTracking().SingleAsync(s => s.Id == Bakery)).DistrictId);

        var cleared = await _service.ApplyReportAsync(Report());
        Assert.Equal(2, cleared.StructuresCleared);
        Assert.All(_db.Structures.AsNoTracking(), s => Assert.Equal(StructureTerritoryFlag.None, s.TerritoryFlag));
    }

    [Fact]
    public async Task Report_UnknownStructureOrFlag_IsSkipped()
    {
        var report = Report();
        report.Structures = new List<TerritoryReportStructureDto>
        {
            new() { StructureId = 999, Flag = "Contested" },
            new() { StructureId = Bakery, Flag = "None" },
        };

        var result = await _service.ApplyReportAsync(report);

        Assert.Equal(2, result.Skipped.Count);
        Assert.Equal(0, result.StructuresFlagged);
    }

    [Fact]
    public async Task Report_WithoutAWorld_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ApplyReportAsync(new TerritoryReportDto { World = " " }));
    }

    [Fact]
    public async Task TownTerritory_ShowsTheEffectiveRanges()
    {
        var town = await _service.GetTownTerritoryAsync(Phoenix);

        Assert.Equal((200.0, 300.0), (town!.Range, town.CapitalRange));
        Assert.Equal(300, town.Districts.Single(d => d.IsCapital).Range);
        Assert.Null(await _service.GetTownTerritoryAsync(999));
    }

    [Fact]
    public async Task Loyalty_IsStored_AndClearedWithNull()
    {
        var set = await _service.SetLoyaltyAsync(Bakery, new StructureLoyaltyUpdateDto { LoyaltyTownId = Rivertown });
        Assert.Equal((Rivertown, "Rivertown", Phoenix), (set.LoyaltyTownId!.Value, set.LoyaltyTownName, set.TownId!.Value));

        var cleared = await _service.SetLoyaltyAsync(Bakery, new StructureLoyaltyUpdateDto { LoyaltyTownId = null });
        Assert.Null(cleared.LoyaltyTownId);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.SetLoyaltyAsync(Bakery, new StructureLoyaltyUpdateDto { LoyaltyTownId = 999 }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.SetLoyaltyAsync(999, new StructureLoyaltyUpdateDto()));
    }

    [Fact]
    public async Task ClearFlag_ResetsTheStructure()
    {
        var report = Report();
        report.Structures = new List<TerritoryReportStructureDto> { new() { StructureId = Mill, Flag = "OutsideTerritory" } };
        await _service.ApplyReportAsync(report);

        var cleared = await _service.ClearFlagAsync(Mill);

        Assert.Equal("None", cleared.TerritoryFlag);
        Assert.Null(cleared.TerritoryFlaggedAt);
    }
}
