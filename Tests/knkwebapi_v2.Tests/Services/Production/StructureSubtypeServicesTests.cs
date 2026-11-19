using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static knkwebapi_v2.Tests.Services.Production.ProductionWorldTestDb;

namespace knkwebapi_v2.Tests.Services.Production;

/// <summary>
/// Warehouse and ProductionStructure CRUD (P2, PandiO/knk-workspace#91, production DESIGN § 3.3): District-xor-Town
/// (R1, A06), the effective town, default storages on create (Kind Domain held by the structure, L4 note), delete rules,
/// field validation, the road connection (§ 7.6) and the dashboard warnings (R3, D5). CRUD works with production off.
/// </summary>
public class StructureSubtypeServicesTests : IDisposable
{
    private readonly KnKDbContext _db = Create(productionEnabled: false);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Warehouse_InADistrict_GetsItsDeliveryStorage_AndPhoenixAsEffectiveTown()
    {
        var created = await Warehouses(_db).CreateAsync(InMarket());

        Assert.Equal((Market, (int?)null, Phoenix, false), (created.DistrictId, created.TownId, created.EffectiveTownId, created.IsRural));
        var storage = Assert.Single(created.Storages!);
        Assert.Equal("Delivery", storage.Purpose);
        Assert.Equal(storage.Id, created.DefaultStorageId);
        var row = _db.Storages.AsNoTracking().Single(s => s.Id == storage.Id);
        Assert.Equal((StorageKind.Domain, (int?)created.Id), (row.Kind, row.DomainId));
        Assert.True(created.AcceptsDeliveries);
    }

    [Fact]
    public async Task ProductionStructure_Rural_GetsItsOutputStorage_AndNoStreet()
    {
        var dto = Rural();
        dto.HouseNumber = 7; // ignored for a rural structure
        var created = await Producers(_db).CreateAsync(dto);

        Assert.Equal(((int?)null, (int?)Phoenix, (int?)Phoenix, true), (created.DistrictId, created.TownId, created.EffectiveTownId, created.IsRural));
        Assert.Equal(((int?)null, 0), (created.StreetId, created.HouseNumber));
        Assert.Equal("Phoenix", created.Town!.Name);
        Assert.Equal("Output", Assert.Single(created.Storages!).Purpose);
        Assert.Equal((1, true, 2, 2, "Nearest", "Unmeasured"),
            (created.Level, created.ProductionEnabled, created.WorkersTotal, created.WorkersPresent, created.WarehousePreference, created.RoadConnection));
    }

    [Theory]
    [InlineData(Market, Phoenix)]   // both
    [InlineData(null, null)]        // neither
    [InlineData(0, 0)]              // 0 = not set
    public async Task BothOrNeither_DistrictAndTown_IsRefused(int? districtId, int? townId)
    {
        var dto = Rural();
        dto.DistrictId = districtId;
        dto.TownId = townId;
        dto.StreetId = Main;
        dto.HouseNumber = 1;

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Producers(_db).CreateAsync(dto));
        Assert.Contains("exactly one", ex.Message);
        Assert.Empty(_db.ProductionStructures);
    }

    [Fact]
    public async Task Rural_WithAStreet_AndDistrict_WithoutStreetOrHouseNumber_AreRefused()
    {
        var rural = Rural();
        rural.StreetId = Main;
        await Assert.ThrowsAsync<ArgumentException>(() => Producers(_db).CreateAsync(rural));

        var noStreet = InMarket();
        noStreet.StreetId = null;
        await Assert.ThrowsAsync<ArgumentException>(() => Warehouses(_db).CreateAsync(noStreet));

        var noNumber = InMarket();
        noNumber.HouseNumber = 0;
        await Assert.ThrowsAsync<ArgumentException>(() => Warehouses(_db).CreateAsync(noNumber));
    }

    [Fact]
    public async Task UnknownTownOrDistrict_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Producers(_db).CreateAsync(Rural(townId: 999)));
        var dto = InMarket();
        dto.DistrictId = 999;
        await Assert.ThrowsAsync<ArgumentException>(() => Warehouses(_db).CreateAsync(dto));
    }

    [Fact]
    public async Task Update_MovesAStructureFromItsDistrictToRural_AndKeepsItsStorages()
    {
        var service = Producers(_db);
        var dto = new ProductionStructureDto { Name = "Mill", Description = "", WgRegionId = "tmp", DistrictId = Market, StreetId = Main, HouseNumber = 2 };
        var created = await service.CreateAsync(dto);

        var update = Rural("Mill", Rivertown);
        update.Level = 4;
        await service.UpdateAsync(created.Id!.Value, update);

        var read = (await service.GetByIdAsync(created.Id.Value))!;
        Assert.Equal(((int?)null, (int?)Rivertown, (int?)Rivertown, (int?)null, 4), (read.DistrictId, read.TownId, read.EffectiveTownId, read.StreetId, read.Level));
        Assert.Single(read.Storages!);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task Level_OutsideOneToTen_IsRefused(int level)
    {
        var dto = Rural();
        dto.Level = level;
        await Assert.ThrowsAsync<ArgumentException>(() => Producers(_db).CreateAsync(dto));
    }

    [Fact]
    public async Task PreferredWarehouse_MustExist_AndPreferredNeedsOne()
    {
        var bad = Rural();
        bad.PreferredWarehouseId = 999;
        await Assert.ThrowsAsync<ArgumentException>(() => Producers(_db).CreateAsync(bad));

        var noWarehouse = Rural();
        noWarehouse.WarehousePreference = "Preferred";
        await Assert.ThrowsAsync<ArgumentException>(() => Producers(_db).CreateAsync(noWarehouse));

        var warehouse = await Warehouses(_db).CreateAsync(InMarket());
        var ok = Rural();
        ok.WarehousePreference = "preferred";
        ok.PreferredWarehouseId = warehouse.Id;
        var created = await Producers(_db).CreateAsync(ok);
        Assert.Equal(("Preferred", warehouse.Id), (created.WarehousePreference, (int?)created.PreferredWarehouse!.Id));
    }

    [Fact]
    public async Task DeletingAWarehouse_ResetsProducersThatPreferIt_AndRemovesItsEmptyStorage()
    {
        var warehouse = await Warehouses(_db).CreateAsync(InMarket());
        var dto = Rural();
        dto.WarehousePreference = "Preferred";
        dto.PreferredWarehouseId = warehouse.Id;
        var producer = await Producers(_db).CreateAsync(dto);

        await Warehouses(_db).DeleteAsync(warehouse.Id!.Value);

        Assert.Empty(_db.Warehouses);
        Assert.DoesNotContain(_db.Storages.AsNoTracking(), s => s.DomainId == warehouse.Id);
        var p = _db.ProductionStructures.AsNoTracking().Single(x => x.Id == producer.Id);
        Assert.Equal((WarehousePreference.Nearest, (int?)null), (p.WarehousePreference, p.PreferredWarehouseId));
    }

    [Fact]
    public async Task Delete_WhileItsStorageHoldsStock_IsRefused_AndKeepsEverything()
    {
        var created = await Producers(_db).CreateAsync(Rural());
        var outputId = created.Storages![0].Id;
        _db.ItemBlueprints.Add(new ItemBlueprint { Id = 200, Name = "Stone", DefaultDisplayName = "Stone" });
        await _db.SaveChangesAsync();
        var moved = await new StockService(_db).AdjustAsync(outputId, 200, null, 5,
            new StockContext { Reason = StockReason.AdminAdjust, ActorComponent = "test", Note = "count" });
        Assert.Equal(StockResultStatus.Applied, moved.Status);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Producers(_db).DeleteAsync(created.Id!.Value));
        _db.ChangeTracker.Clear();
        Assert.Single(_db.ProductionStructures);
        Assert.Contains(_db.Storages, s => s.Id == outputId);
    }

    [Fact]
    public async Task RoadConnection_IsSetByTheGameServer_AndUnconnectedRuralShowsAWarning()
    {
        var created = await Producers(_db).CreateAsync(Rural());

        var read = await Producers(_db).SetRoadConnectionAsync(created.Id!.Value, new RoadConnectionUpdateDto { RoadConnection = "unconnected" });

        Assert.Equal("Unconnected", read.RoadConnection);
        Assert.NotNull(read.RoadConnectionMeasuredAt);
        Assert.Contains(read.Warnings!, w => w.Contains("road network"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Producers(_db).SetRoadConnectionAsync(created.Id.Value, new RoadConnectionUpdateDto { RoadConnection = "Maybe" }));
    }

    [Fact]
    public async Task Search_FiltersByTownRuralAndFlag_AndListsTheEffectiveTown()
    {
        await Warehouses(_db).CreateAsync(InMarket());
        await Warehouses(_db).CreateAsync(new WarehouseDto { Name = "Field store", Description = "", WgRegionId = "w2", TownId = Rivertown });

        var phoenix = await Warehouses(_db).SearchAsync(new PagedQueryDto { Filters = new() { ["townId"] = Phoenix.ToString() } });
        var rural = await Warehouses(_db).SearchAsync(new PagedQueryDto { Filters = new() { ["isRural"] = "true" } });

        var row = Assert.Single(phoenix.Items);
        Assert.Equal(("Market warehouse", (int?)Phoenix, false), (row.name, row.townId, row.isRural));
        Assert.Equal(("Field store", (int?)Rivertown, "Rivertown"), (Assert.Single(rural.Items).name, rural.Items[0].townId, rural.Items[0].townName));
    }
}
