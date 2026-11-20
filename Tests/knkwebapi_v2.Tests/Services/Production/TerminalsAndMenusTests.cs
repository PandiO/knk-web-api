using System.Text.Json;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using static knkwebapi_v2.Tests.Services.Production.ProductionEngineTestDb;

namespace knkwebapi_v2.Tests.Services.Production;

/// <summary>
/// P6a (PandiO/knk-workspace#683): storage containers (Terminal and Display, container-derived capacity, A14 API side),
/// the caller-filtered structure menus (M3, A25, A26), the staff retry (M4) and the #877 extras. Viewers: Staff holds
/// knk.admin.world, Member holds knk.town.member.&lt;Phoenix&gt; (town membership until #246), Visitor holds nothing.
/// </summary>
public class TerminalsAndMenusTests : IDisposable
{
    private const int Staff = 9001, Member = 9002, Visitor = 9003;

    private readonly TransportTestDb _t = new();
    private readonly ProductionViewerResolver _viewers;
    private readonly StorageContainerService _containers;
    private readonly StructureMenuService _menus;

    public TerminalsAndMenusTests()
    {
        var permissions = new Mock<IPermissionResolutionService>();
        permissions.Setup(p => p.CheckAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync((int user, string node) =>
            new PermissionCheckResponseDto
            {
                UserId = user,
                Node = node,
                Result = (user == Staff && node == StaffPermissions.ManageWorld)
                    || (user == Member && node == PermissionTownMembershipResolver.NodeFor(ProductionWorldTestDb.Phoenix))
                    ? PermissionResolutionResult.Granted
                    : PermissionResolutionResult.Undeclared,
            });
        _viewers = new ProductionViewerResolver(_t.E.Db, permissions.Object, new PermissionTownMembershipResolver(permissions.Object));
        _containers = new StorageContainerService(_t.E.Db, _t.E.Stock, new ProductionSettingsReader(_t.E.Db), _viewers, _t.E.Clock);
        _menus = new StructureMenuService(_t.E.Db, _viewers, _t.E.Engine, _t.Transport);
    }

    public void Dispose() => _t.Dispose();

    private Task<ProductionViewer> As(int? user) =>
        _viewers.ResolveAsync(new KnkCaller(isPluginService: true, isWebUser: false, webUserId: null, actingUserId: user));

    private Task<StorageContainerDto> Bind(int storage, int x, StorageContainerMode mode = StorageContainerMode.Terminal,
        StorageContainerType type = StorageContainerType.Chest, bool isDouble = false) =>
        _containers.CreateAsync(new StorageContainerDto
        {
            StorageId = storage, Mode = mode, ContainerType = type, IsDouble = isDouble, WorldName = "world", X = x, Y = 64, Z = 0,
        }, Staff);

    private Storage StorageRow(int id) => _t.E.Db.Storages.AsNoTracking().Single(s => s.Id == id);

    // ================= Containers =================

    [Fact]
    public async Task BindingContainers_SetsTheCapToSlotsTimesUnitsPerSlot_AndUnbindingTheLastKeepsIt()
    {
        await _t.SetUpAsync();
        _t.E.Update(db => db.Storages.Single(s => s.Id == _t.NorthStorage), s => s.UnitsPerSlot = 200);

        var chest = await Bind(_t.NorthStorage, 1, isDouble: true);
        Assert.Equal((54, 54, 10_800L), (chest.Slots!.Value, chest.StorageContainerSlots!.Value, chest.StorageMaxUnits!.Value));
        var barrel = await Bind(_t.NorthStorage, 3, StorageContainerMode.Display, StorageContainerType.Barrel);
        Assert.Equal(16_200L, StorageRow(_t.NorthStorage).MaxUnits);

        await _containers.DeleteAsync(barrel.Id!.Value);
        Assert.Equal(10_800L, StorageRow(_t.NorthStorage).MaxUnits);
        await _containers.DeleteAsync(chest.Id!.Value);
        Assert.Equal(10_800L, StorageRow(_t.NorthStorage).MaxUnits); // fixed again, nothing trimmed

        // Raising units per slot on the storage re-derives the cap while containers are bound.
        await Bind(_t.NorthStorage, 5);
        var storage = await new StorageService(_t.E.Db).GetByIdAsync(_t.NorthStorage);
        storage!.UnitsPerSlot = 300;
        await new StorageService(_t.E.Db).UpdateAsync(_t.NorthStorage, storage);
        Assert.Equal(27L * 300, StorageRow(_t.NorthStorage).MaxUnits);

        // Without units per slot the fixed cap stays.
        var southCap = StorageRow(_t.SouthStorage).MaxUnits;
        await Bind(_t.SouthStorage, 7);
        Assert.Equal(southCap, StorageRow(_t.SouthStorage).MaxUnits);
    }

    [Fact]
    public async Task Binding_RefusesSystemStorages_ATakenBlock_AndADoubleBarrel()
    {
        await _t.SetUpAsync();
        var world = StorageRow(_t.NorthStorage); // a real storage
        var sys = _t.E.Db.Storages.AsNoTracking().Single(s => s.SystemKey == Storage.SysWorld).Id;
        await Assert.ThrowsAsync<ArgumentException>(() => Bind(sys, 1));
        await Bind(world.Id, 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Bind(_t.SouthStorage, 1));
        await Assert.ThrowsAsync<ArgumentException>(() => Bind(world.Id, 2, type: StorageContainerType.Barrel, isDouble: true));
        Assert.NotNull(await _containers.GetByBlockAsync("world", 1, 64, 0));
        Assert.Null(await _containers.GetByBlockAsync("world", 2, 64, 0));
    }

    [Fact]
    public async Task ATerminal_PutsAndTakesThroughTheLedger_OnceperKey_ForMembersAndStaffOnly()
    {
        await _t.SetUpAsync();
        var terminal = (await Bind(_t.NorthStorage, 1)).Id!.Value;
        var member = await As(Member);

        var put = await _containers.TerminalMoveAsync(terminal, put: true,
            new TerminalMoveDto { ItemBlueprintId = Cobblestone, Amount = 10, IdempotencyKey = "click-1" }, member);
        Assert.Equal(StockResultStatus.Applied, put.Status);
        var again = await _containers.TerminalMoveAsync(terminal, put: true,
            new TerminalMoveDto { ItemBlueprintId = Cobblestone, Amount = 10, IdempotencyKey = "click-1" }, member);
        Assert.True(again.Replayed);
        Assert.Equal(10, _t.Amount(_t.NorthStorage, Cobblestone));

        var take = await _containers.TerminalMoveAsync(terminal, put: false,
            new TerminalMoveDto { ItemBlueprintId = Cobblestone, Amount = 4, IdempotencyKey = "click-2" }, await As(Staff));
        Assert.Equal(StockResultStatus.Applied, take.Status);
        Assert.Equal(6, _t.Amount(_t.NorthStorage, Cobblestone));
        var reasons = _t.E.Db.StockTransactions.AsNoTracking().Where(x => x.SourceType == "StorageContainer")
            .OrderBy(x => x.Id).Select(x => new { x.Reason, x.ActorUserId }).ToList();
        Assert.Equal(new[] { (StockReason.Deposit, (int?)Member), (StockReason.Withdraw, (int?)Staff) },
            reasons.Select(r => (r.Reason, r.ActorUserId)));
        Assert.True(_t.LedgerClean());

        var view = await _containers.TerminalViewAsync(terminal, member, 1, 54);
        var entry = Assert.Single(view.Entries);
        Assert.Equal((Cobblestone, 6L), (entry.ItemBlueprintId, entry.Amount));

        // A visitor (or the game server without a player) may not use it.
        var visitor = await As(Visitor);
        var nobody = await As(null);
        await Assert.ThrowsAsync<StorageAccessDeniedException>(() => _containers.TerminalViewAsync(terminal, visitor, 1, 54));
        await Assert.ThrowsAsync<StorageAccessDeniedException>(() => _containers.TerminalMoveAsync(terminal, put: false,
            new TerminalMoveDto { ItemBlueprintId = Cobblestone, Amount = 1, IdempotencyKey = "v" }, nobody));
        Assert.Equal(6, _t.Amount(_t.NorthStorage, Cobblestone));
    }

    [Fact]
    public async Task ATerminalTake_OfMoreThanThereIs_MovesWhatThereIs_AndADisplayIsNoTerminal()
    {
        await _t.SetUpAsync();
        await _t.Put(_t.NorthStorage, 3);
        var terminal = (await Bind(_t.NorthStorage, 1)).Id!.Value;
        var display = (await Bind(_t.NorthStorage, 2, StorageContainerMode.Display)).Id!.Value;
        var member = await As(Member);

        var take = await _containers.TerminalMoveAsync(terminal, put: false,
            new TerminalMoveDto { ItemBlueprintId = Cobblestone, Amount = 5, IdempotencyKey = "k" }, member);
        Assert.Equal((3L, 2L, StockRejectReason.NotEnoughStock), (take.Accepted, take.Lines[0].Rejected, take.Lines[0].RejectReason));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _containers.TerminalViewAsync(display, member, 1, 54));
        await Assert.ThrowsAsync<ArgumentException>(() => _containers.TerminalMoveAsync(terminal, put: true,
            new TerminalMoveDto { ItemBlueprintId = Cobblestone, Amount = 1 }, member)); // no idempotency key
    }

    [Fact]
    public async Task WithTheFlagOff_TerminalsMoveNothing_ButBindingAndReadsWork()
    {
        using var t = new TransportTestDb(productionEnabled: false);
        await t.SetUpAsync();
        var containers = new StorageContainerService(t.E.Db, t.E.Stock, new ProductionSettingsReader(t.E.Db), _viewers, t.E.Clock);
        var bound = await containers.CreateAsync(new StorageContainerDto
        {
            StorageId = t.NorthStorage, Mode = StorageContainerMode.Terminal, WorldName = "world", X = 1, Y = 64, Z = 0,
        }, Staff);
        await Assert.ThrowsAsync<ProductionOffException>(() => containers.TerminalMoveAsync(bound.Id!.Value, put: true,
            new TerminalMoveDto { ItemBlueprintId = Cobblestone, Amount = 1, IdempotencyKey = "k" }, new ProductionViewer(Staff, true)));
        Assert.Equal(0, t.Amount(t.NorthStorage, Cobblestone));
    }

    [Fact]
    public async Task ADisplay_ShowsASampleProportionalToTheFillLevel()
    {
        await _t.SetUpAsync(northCap: 1000);
        await _t.Put(_t.NorthStorage, 300);
        await _t.Put(_t.NorthStorage, 200, Coal);
        var display = (await Bind(_t.NorthStorage, 2, StorageContainerMode.Display)).Id!.Value;

        var sample = await _containers.DisplaySampleAsync(display);
        Assert.Equal(0.5m, sample.FillRatio);
        Assert.Equal(14, sample.FilledSlots); // half of 27, rounded up
        Assert.Equal(new[] { (Cobblestone, 8), (Coal, 6) }, sample.Items.Select(i => (i.ItemBlueprintId, i.Stacks)));

        var empty = (await Bind(_t.SouthStorage, 3, StorageContainerMode.Display)).Id!.Value;
        Assert.Equal((0m, 0), ((await _containers.DisplaySampleAsync(empty)).FillRatio, (await _containers.DisplaySampleAsync(empty)).FilledSlots));
    }

    // ================= Structure menus (M3, A25, A26) =================

    [Fact]
    public async Task AVisitor_GetsOnlyThePublicFields_AndTheResponseHoldsNoStockRatesWorkersOrOrders()
    {
        await _t.SetUpAsync();
        await _t.Put(_t.QuarryOutput, 40);

        var visitor = await _menus.GetAsync(_t.Quarry, await As(Visitor));
        Assert.NotNull(visitor);
        Assert.Equal(("Quarry", "Quarry", "Phoenix", 1), (visitor!.Name, visitor.KindName, visitor.TownName, visitor.Level));
        Assert.Equal(new[] { "Cobblestone" }, visitor.Products);
        Assert.False(visitor.ViewerSeesDetails);
        Assert.Null(visitor.Detail);
        var json = JsonSerializer.Serialize(visitor);
        foreach (var hidden in new[] { "workersPresent", "rates", "storages", "openOrderCount", "\"amount\"" })
            Assert.DoesNotContain(hidden, json);
        var visitorViewer = await As(Visitor);
        await Assert.ThrowsAsync<StorageAccessDeniedException>(() => _menus.OrdersAsync(_t.Quarry, visitorViewer, new PagedQueryDto()));

        // The game server without a player is a visitor too.
        Assert.Null((await _menus.GetAsync(_t.Quarry, await As(null)))!.Detail);
    }

    [Fact]
    public async Task MembersAndStaff_SeeRatesStorageWorkersWarningsAndOrders_OnlyStaffAreStaff()
    {
        await _t.SetUpAsync();
        await _t.Put(_t.QuarryOutput, 40);
        var order = await _t.Transport.CreateAsync(_t.Manual(_t.NorthStorage, 10), Staff);

        foreach (var (user, staff) in new[] { (Member, false), (Staff, true) })
        {
            var menu = (await _menus.GetAsync(_t.Quarry, await As(user)))!;
            Assert.True(menu.ViewerSeesDetails);
            Assert.Equal(staff, menu.ViewerIsStaff);
            var detail = Assert.IsType<StructureMenuDetailDto>(menu.Detail);
            Assert.Equal((2, 2, 0), (detail.WorkersPresent, detail.WorkersTotal, detail.WorkersAway));
            Assert.Equal(Cobblestone, Assert.Single(detail.Rates).ItemBlueprintId);
            var output = detail.Storages.Single(s => s.StorageId == _t.QuarryOutput);
            Assert.Equal(40, Assert.Single(output.Lines).Amount);
            Assert.Equal(1, detail.OpenOrderCount);
            var orders = await _menus.OrdersAsync(_t.Quarry, await As(user), new PagedQueryDto());
            Assert.Equal(order.Id, Assert.Single(orders!.Items).Id);
        }

        // A warehouse lists its delivery storage and the orders bound for it.
        var north = (await _menus.GetAsync(_t.North, await As(Member)))!;
        Assert.Equal(("Warehouse", 1), (north.StructureType, north.Detail!.OpenOrderCount));
        Assert.Contains(north.Detail.Storages, s => s.StorageId == _t.NorthStorage);
    }

    [Fact]
    public async Task TheOverview_ListsProducersAndWarehouses_WithStatuses_AndFilters()
    {
        await _t.SetUpAsync(connected: false);
        var bare = (await _t.E.QuarryAsync(name: "Full quarry")).StructureId;
        _t.E.Update(db => db.ProductionLines.Single(l => l.ProductionStructureId == bare), l => l.Status = ProductionLineStatus.BlockedFull);

        var all = await _menus.SearchAsync(new PagedQueryDto { PageSize = 50 });
        var byName = all.Items.ToDictionary(r => r.Name);
        Assert.Equal(StructureMenuStatus.NoRoad, byName["Quarry"].Status);   // rural, not connected (D5)
        Assert.Equal(StructureMenuStatus.Full, byName["Full quarry"].Status);
        Assert.Equal((StructureMenuStatus.Working, "Warehouse"), (byName["North"].Status, byName["North"].KindName));

        var warehouses = await _menus.SearchAsync(new PagedQueryDto { Filters = new() { ["structureType"] = "Warehouse" } });
        Assert.Equal(new[] { "North", "South" }, warehouses.Items.Select(r => r.Name));
        var full = await _menus.SearchAsync(new PagedQueryDto { Filters = new() { ["status"] = "Full", ["townId"] = ProductionWorldTestDb.Phoenix.ToString() } });
        Assert.Equal("Full quarry", Assert.Single(full.Items).Name);
        Assert.Empty((await _menus.SearchAsync(new PagedQueryDto { Filters = new() { ["townId"] = ProductionWorldTestDb.Rivertown.ToString() } })).Items);
        Assert.Equal(new[] { "Full quarry", "Quarry" }, (await _menus.SearchAsync(new PagedQueryDto { SearchTerm = "quarry" })).Items.Select(r => r.Name));
    }

    [Theory]
    [InlineData(true, false, RoadConnectionState.Connected, 2, ProductionLineStatus.Running, "Working")]
    [InlineData(false, false, RoadConnectionState.Connected, 2, ProductionLineStatus.Running, "Stalled")]
    [InlineData(true, false, RoadConnectionState.Connected, 0, ProductionLineStatus.Running, "Stalled")]
    [InlineData(true, false, RoadConnectionState.Connected, 2, ProductionLineStatus.BlockedInputs, "Stalled")]
    [InlineData(true, true, RoadConnectionState.Unmeasured, 2, ProductionLineStatus.Running, "NoRoad")]
    [InlineData(true, false, RoadConnectionState.Unmeasured, 2, ProductionLineStatus.Running, "Working")]
    [InlineData(true, true, RoadConnectionState.Unconnected, 2, ProductionLineStatus.BlockedFull, "Full")]
    public void ThePublicStatus(bool enabled, bool rural, RoadConnectionState road, int workers, ProductionLineStatus line, string expected) =>
        Assert.Equal(expected, StructureMenuService.ProducerStatus(enabled, rural, road, workers, new[] { (true, line) }).Status);

    // ================= Staff retry (M4) =================

    [Fact]
    public async Task Retry_RunsAWaitingOrderNow_AndSendsACancelledOneAgainOnce()
    {
        await _t.SetUpAsync();
        await _t.Put(_t.QuarryOutput, 20);
        var later = _t.Manual(_t.NorthStorage, 10);
        later.ExecuteAfter = _t.E.Clock.GetUtcNow().UtcDateTime.AddHours(1);
        var order = await _t.Transport.CreateAsync(later, Staff);

        var now = await _t.Transport.RetryAsync(order.Id, Staff);
        Assert.Equal("InTransit", now.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _t.Transport.RetryAsync(order.Id, Staff)); // on the road

        var second = await _t.Transport.CreateAsync(_t.Manual(_t.SouthStorage, 5), Staff);
        await _t.Transport.CancelAsync(second.Id, Staff, "test");
        var again = await _t.Transport.RetryAsync(second.Id, Staff);
        Assert.NotEqual(second.Id, again.Id);
        Assert.Equal(again.Id, (await _t.Transport.RetryAsync(second.Id, Staff)).Id);
        Assert.Equal(5, Assert.Single(_t.Order(again.Id).Lines).Requested);

        _t.Advance(100);
        await _t.Sweep();
        Assert.Equal(TransportStatus.Delivered, _t.Order(order.Id).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _t.Transport.RetryAsync(order.Id, Staff));
    }

    [Fact]
    public async Task Retry_Is409WhileTheFlagIsOff()
    {
        using var t = new TransportTestDb(productionEnabled: false);
        await t.SetUpAsync();
        await t.Put(t.QuarryOutput, 20);
        var order = await t.Transport.CreateAsync(t.Manual(t.NorthStorage, 10), Staff);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => t.Transport.RetryAsync(order.Id, Staff));
        Assert.Contains("Production:Enabled", ex.Message);
    }
}

/// <summary>#877: a Region area takes its bounds from the region's stored outline; the structure list has the yield
/// source.</summary>
public class ProductionAreaRegionBoundsTests : IDisposable
{
    private readonly ProductionYieldTestDb _y = new();

    public void Dispose() => _y.Dispose();

    [Fact]
    public async Task ARegionArea_WithoutBounds_TakesThemFromTheRegionOutline()
    {
        var (quarry, _, _) = await _y.T.QuarryAsync();
        _y.T.Db.MapRegionOutlines.Add(new MapRegionOutline
        {
            WgRegionId = "pit_region", WorldName = "world", MinY = 0, MaxY = 63, UpdatedAt = DateTime.UtcNow,
            PointsJson = "[[100,100],[163,100],[163,163],[100,163]]",
        });
        _y.T.Db.SaveChanges();

        var area = await _y.Areas.CreateAsync(new ProductionAreaDto
        {
            ProductionStructureId = quarry, Name = "Pit", Shape = "Region", WgRegionId = "Pit_Region", WorldName = "world",
        });
        Assert.Equal((100, 163, 0, 63, 100, 163), (area.MinX!.Value, area.MaxX!.Value, area.MinY!.Value, area.MaxY!.Value, area.MinZ!.Value, area.MaxZ!.Value));
        Assert.Equal(262144, area.Size);

        // Without an outline the bounds stay required.
        await Assert.ThrowsAsync<ArgumentException>(() => _y.Areas.CreateAsync(new ProductionAreaDto
        {
            ProductionStructureId = quarry, Name = "Unknown", Shape = "Region", WgRegionId = "nowhere", WorldName = "world",
        }));
    }

    [Fact]
    public async Task TheStructureList_HasTheYieldSource()
    {
        var (quarry, _, _) = await _y.T.QuarryAsync();
        var list = await _y.Structures.SearchAsync(new PagedQueryDto());
        Assert.Equal("Hybrid", list.Items.Single(r => r.id == quarry).yieldSource);
    }
}
