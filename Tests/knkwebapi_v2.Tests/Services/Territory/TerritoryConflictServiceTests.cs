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
/// Conflicts are resolved by admins only (territory DESIGN § 3, PandiO/knk-workspace#326): an award goes to one of the
/// two holders and reaches the game server through the inputs; "redrawn" records a manual fix; reopen undoes either.
/// </summary>
public class TerritoryConflictServiceTests : IDisposable
{
    private readonly KnKDbContext _db;
    private readonly TerritoryConflictService _conflicts;
    private readonly TerritoryService _territory;
    private readonly int _id;

    public TerritoryConflictServiceTests()
    {
        _db = Create();
        _conflicts = new TerritoryConflictService(_db);
        _territory = new TerritoryService(_db, new ProductionSettingsReader(_db), NullLogger<TerritoryService>.Instance);
        _territory.ApplyReportAsync(new TerritoryReportDto
        {
            World = "world",
            Overlaps = { new TerritoryReportOverlapDto { HolderA = Town(Phoenix), HolderB = Town(Rivertown), Chunks = { new[] { 7, 3 } } } },
        }).GetAwaiter().GetResult();
        _id = _db.TerritoryConflicts.Single().Id;
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Read_NamesTheHolders_AndTheArea()
    {
        var dto = await _conflicts.GetByIdAsync(_id);

        Assert.Equal(("Phoenix", "Rivertown"), (dto!.HolderA.Name, dto.HolderB.Name));
        Assert.Equal(new[] { 7, 3 }, Assert.Single(dto.Chunks));
        var page = await _conflicts.SearchAsync(new PagedQueryDto { Filters = new() { ["status"] = "Open" } });
        Assert.Equal("Phoenix vs Rivertown", Assert.Single(page.Items).Name);
    }

    [Fact]
    public async Task Award_ToOneHolder_ResolvesIt_AndReachesTheInputs()
    {
        var resolved = await _conflicts.ResolveAsync(_id, new TerritoryConflictResolveDto
        {
            Resolution = "Awarded", AwardedHolder = Town(Phoenix), Note = "Phoenix had it first",
        }, userId: 5);

        Assert.Equal(("Resolved", "Awarded", "Phoenix", 5), (resolved.Status, resolved.Resolution, resolved.AwardedHolder!.Name, resolved.ResolvedByUserId!.Value));
        var award = Assert.Single((await _territory.GetInputsAsync()).Awards);
        Assert.Equal(("Town", Phoenix), (award.Holder.Kind, award.Holder.Id));
        Assert.Equal(new[] { 7, 3 }, Assert.Single(award.Chunks));
    }

    [Fact]
    public async Task Award_ToAThirdHolder_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _conflicts.ResolveAsync(_id,
            new TerritoryConflictResolveDto { Resolution = "Awarded", AwardedHolder = KingdomHolder(KingdomId) }, null));
        await Assert.ThrowsAsync<ArgumentException>(() => _conflicts.ResolveAsync(_id,
            new TerritoryConflictResolveDto { Resolution = "Awarded" }, null));
        await Assert.ThrowsAsync<ArgumentException>(() => _conflicts.ResolveAsync(_id,
            new TerritoryConflictResolveDto { Resolution = "None" }, null));
    }

    [Fact]
    public async Task Redrawn_ResolvesWithoutAnAward_AndTwiceIsRefused()
    {
        var resolved = await _conflicts.ResolveAsync(_id, new TerritoryConflictResolveDto { Resolution = "redrawn" }, null);

        Assert.Equal("Redrawn", resolved.Resolution);
        Assert.Null(resolved.AwardedHolder);
        Assert.Empty((await _territory.GetInputsAsync()).Awards);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _conflicts.ResolveAsync(_id, new TerritoryConflictResolveDto { Resolution = "Redrawn" }, null));
    }

    [Fact]
    public async Task Reopen_UndoesTheResolution()
    {
        await _conflicts.ResolveAsync(_id, new TerritoryConflictResolveDto { Resolution = "Awarded", AwardedHolder = Town(Rivertown) }, null);

        var reopened = await _conflicts.ReopenAsync(_id);

        Assert.Equal(("Open", "None"), (reopened.Status, reopened.Resolution));
        Assert.Null(reopened.AwardedHolder);
        Assert.Empty((await _territory.GetInputsAsync()).Awards);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _conflicts.ReopenAsync(_id));
    }

    [Fact]
    public async Task AResolvedConflict_IsNotReopenedByTheNextReport_ANewOneOpensInstead()
    {
        await _conflicts.ResolveAsync(_id, new TerritoryConflictResolveDto { Resolution = "Redrawn" }, null);

        await _territory.ApplyReportAsync(new TerritoryReportDto
        {
            World = "world",
            Overlaps = { new TerritoryReportOverlapDto { HolderA = Town(Phoenix), HolderB = Town(Rivertown), Chunks = { new[] { 8, 3 } } } },
        });

        Assert.Equal(2, await _db.TerritoryConflicts.CountAsync());
        Assert.Equal(TerritoryConflictStatus.Resolved, (await _db.TerritoryConflicts.AsNoTracking().SingleAsync(c => c.Id == _id)).Status);
    }

    [Fact]
    public async Task Delete_UnlinksItsFlaggedStructures()
    {
        await _territory.ApplyReportAsync(new TerritoryReportDto
        {
            World = "world",
            Overlaps = { new TerritoryReportOverlapDto { HolderA = Town(Phoenix), HolderB = Town(Rivertown), Chunks = { new[] { 7, 3 } } } },
            Structures = { new TerritoryReportStructureDto { StructureId = Bakery, Flag = "Contested", HolderA = Town(Phoenix), HolderB = Town(Rivertown) } },
        });

        await _conflicts.DeleteAsync(_id);

        Assert.Empty(_db.TerritoryConflicts);
        Assert.Null((await _db.Structures.AsNoTracking().SingleAsync(s => s.Id == Bakery)).TerritoryConflictId);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _conflicts.DeleteAsync(_id));
    }
}
