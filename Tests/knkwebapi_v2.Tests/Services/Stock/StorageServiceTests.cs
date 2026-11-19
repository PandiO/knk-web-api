using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static knkwebapi_v2.Tests.Services.Stock.StockTestDb;

namespace knkwebapi_v2.Tests.Services.Stock;

/// <summary>
/// Storage CRUD (production DESIGN § 4.1, #97): one holder per kind (domain, player, clan, none for transit), system
/// storages are seeded only, kind and holder are fixed once there is stock or history, an update over a newer version is
/// refused, a storage with stock or history can't be deleted (Restrict, the ledger keeps it), plus the search filters,
/// the detail read with lines, units and capacity used, and the blueprint's storage weight.
/// </summary>
public class StorageServiceTests : IDisposable
{
    private readonly KnKDbContext _db = Create();
    private readonly StorageService _service;
    private readonly StockService _stock;

    public StorageServiceTests()
    {
        _service = new StorageService(_db);
        _stock = new StockService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Create_ForAStructurePlayerClanAndTransit_EachWithItsOwnHolder()
    {
        var domain = await _service.CreateAsync(new StorageDto { Name = " Docks ", Kind = StorageKind.Domain, DomainId = Phoenix, MaxUnits = 5400 });
        var player = await _service.CreateAsync(new StorageDto { Name = "Inbox", Kind = StorageKind.Player, UserId = PlayerId, Purpose = StoragePurpose.Inbox });
        var clan = await _service.CreateAsync(new StorageDto { Name = "Clan vault", Kind = StorageKind.Clan, ClanId = ClanId });
        var transit = await _service.CreateAsync(new StorageDto { Name = "Cargo", Kind = StorageKind.Transit });

        Assert.Equal(("Docks", "Phoenix", 5400L), (domain.Name, domain.HolderName, domain.MaxUnits!.Value));
        Assert.Equal(("Steve", StoragePurpose.Inbox), (player.HolderName, player.Purpose!.Value));
        Assert.Equal("Redcloaks", clan.HolderName);
        Assert.Equal("Transit", transit.HolderName);
    }

    [Theory]
    [InlineData(StorageKind.Domain, null, PlayerId, null)]     // wrong holder column
    [InlineData(StorageKind.Domain, null, null, null)]         // missing holder
    [InlineData(StorageKind.Domain, Phoenix, PlayerId, null)]  // two holders
    [InlineData(StorageKind.Player, null, 999, null)]          // unknown user
    [InlineData(StorageKind.Clan, null, null, 999)]            // unknown clan
    [InlineData(StorageKind.Transit, Phoenix, null, null)]     // transit has no holder
    [InlineData(StorageKind.Virtual, null, null, null)]        // system storages are seeded
    public async Task Create_WithAHolderThatDoesNotMatchTheKind_IsRefused(StorageKind kind, int? domainId, int? userId, int? clanId)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(new StorageDto { Name = "x", Kind = kind, DomainId = domainId, UserId = userId, ClanId = clanId }));
    }

    [Fact]
    public async Task Create_TransitWithACap_AndNegativeCaps_AreRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new StorageDto { Name = "c", Kind = StorageKind.Transit, MaxUnits = 5 }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(new StorageDto { Name = "c", Kind = StorageKind.Domain, DomainId = Phoenix, MaxUnits = -1 }));
    }

    [Fact]
    public async Task Update_ChangesCapacityAndLock_AndBumpsTheRowVersion()
    {
        var before = await _service.GetByIdAsync(S1);
        var until = DateTime.UtcNow.AddHours(2);

        await _service.UpdateAsync(S1, new StorageDto { Name = "S1", MaxUnits = 500, MaxDistinct = 3, LockedUntil = until, LockReason = "audit", RowVersion = before!.RowVersion });

        var after = await _service.GetByIdAsync(S1);
        Assert.Equal((500L, 3, "audit", true), (after!.MaxUnits!.Value, after.MaxDistinct!.Value, after.LockReason, after.IsLocked!.Value));
        Assert.Equal(before.RowVersion + 1, after.RowVersion);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(S1, new StorageDto { Name = "S1", RowVersion = before.RowVersion }));
    }

    [Fact]
    public async Task Update_KindOrHolder_OnlyWithoutStockOrHistory()
    {
        await _service.UpdateAsync(S2, new StorageDto { Name = "S2", Kind = StorageKind.Player, UserId = PlayerId });
        Assert.Equal(StorageKind.Player, (await _service.GetByIdAsync(S2))!.Kind);

        await _stock.AdjustAsync(S1, OakLog, null, 1, Adjust());
        await _stock.AdjustAsync(S1, OakLog, null, -1, Adjust());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(S1, new StorageDto { Name = "S1", Kind = StorageKind.Clan, ClanId = ClanId }));
    }

    [Fact]
    public async Task SystemStorages_CannotBeEditedOrDeleted()
    {
        int sys = await _stock.GetSystemStorageIdAsync(Models.Storage.SysLoss);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAsync(sys, new StorageDto { Name = "x" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteAsync(sys));
    }

    [Fact]
    public async Task Delete_OnlyAnEmptyStorageWithoutHistory()
    {
        await _stock.AdjustAsync(S1, OakLog, null, 5, Adjust());
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteAsync(S1));
        await _stock.AdjustAsync(S1, OakLog, null, -5, Adjust());
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteAsync(S1)); // still has history

        await _service.DeleteAsync(S2);
        Assert.Null(await _service.GetByIdAsync(S2));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.DeleteAsync(S2));
    }

    [Fact]
    public async Task Detail_ListsLinesAndUnits_WithCapacityUsedInWeightedUnits()
    {
        await _stock.AdjustAsync(S1, OakLog, null, 10, Adjust());
        await _stock.AdjustAsync(S1, Stone, null, 5, Adjust());
        await _stock.AdjustAsync(S1, null, SwordInstance, 1, Adjust());

        var dto = (await _service.GetByIdAsync(S1))!;

        Assert.Equal(new[] { "Oak log", "Stone" }, dto.Lines!.Select(l => l.ItemBlueprintName));
        Assert.Equal(SwordInstance, Assert.Single(dto.Units!).ItemInstanceId);
        Assert.Equal("Dawnbreaker", dto.Units![0].CustomDisplayName);
        Assert.Equal(10 + 5 * 2 + 1, dto.CapacityUsed);
        Assert.Equal((16L, 3), (dto.TotalUnits!.Value, dto.DistinctItems!.Value));
    }

    [Fact]
    public async Task Search_FiltersByKindAndHolder()
    {
        var byKind = await _service.SearchAsync(new PagedQueryDto { Filters = new() { ["kind"] = "Domain" } });
        var byUser = await _service.SearchAsync(new PagedQueryDto { Filters = new() { ["userId"] = PlayerId.ToString() } });
        var byTerm = await _service.SearchAsync(new PagedQueryDto { SearchTerm = "redcl" });

        Assert.Equal(new[] { S1, S2 }, byKind.Items.Select(s => s.Id));
        Assert.Equal(Vault, Assert.Single(byUser.Items).Id);
        Assert.Equal(ClanVault, Assert.Single(byTerm.Items).Id);
    }

    [Fact]
    public void StorageWeight_IsValidated_AndNullKeeps()
    {
        Assert.Null(ItemBlueprintService.ValidStorageWeight(null));
        Assert.Equal(2.5m, ItemBlueprintService.ValidStorageWeight(2.5m));
        Assert.Throws<ArgumentException>(() => ItemBlueprintService.ValidStorageWeight(0m));
        Assert.Throws<ArgumentException>(() => ItemBlueprintService.ValidStorageWeight(-1m));
        Assert.Equal(1m, new ItemBlueprint().StorageWeight);
    }
}
