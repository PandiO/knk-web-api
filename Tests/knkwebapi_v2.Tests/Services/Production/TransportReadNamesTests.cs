using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Production;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static knkwebapi_v2.Tests.Services.Production.ProductionEngineTestDb;

namespace knkwebapi_v2.Tests.Services.Production;

/// <summary>
/// The P5 read additions of PandiO/knk-workspace#890: names on the transport reads (the structure view's destination
/// warehouse, the plan's carrier, a cycle record's structure), the structure kind on the lanes due for measuring, and the
/// #875 unit types on the survey queue's overlaps.
/// </summary>
public class TransportReadNamesTests : IDisposable
{
    private readonly TransportTestDb _t = new();

    public void Dispose() => _t.Dispose();

    [Fact]
    public async Task TheStructureView_NamesItsDestinationWarehouse_AndThePlanItsCarrier()
    {
        await _t.SetUpAsync();
        var view = (await _t.Transport.StructureViewAsync(_t.Quarry))!;
        Assert.Equal((_t.North, "North"), (view.DestinationWarehouseId!.Value, view.DestinationWarehouseName));

        var plan = await _t.Transport.DryRunAsync(_t.Manual(_t.NorthStorage, 1));
        Assert.Equal((_t.Quarry, "Quarry"), (plan.CarrierStructureId!.Value, plan.CarrierStructureName));
    }

    [Fact]
    public async Task TheDueLanes_SayWhichEndIsAProducerAndWhichAWarehouse()
    {
        await _t.SetUpAsync(lanes: false);
        var due = await _t.Lanes.DueAsync(100);
        Assert.Equal(new[] { ("Producer", "Warehouse"), ("Producer", "Warehouse"), ("Warehouse", "Warehouse") },
            due.Select(d => (d.FromKind, d.ToKind)).ToArray());
    }

    [Fact]
    public async Task ACycleRecord_NamesItsStructure()
    {
        await _t.SetUpAsync();
        var record = new ProductionCycleRecord
        {
            ProductionLineId = _t.QuarryLine, ProductionStructureId = _t.Quarry, ItemBlueprintId = Cobblestone, ModifiersJson = "[]",
            SettledAt = DateTime.UtcNow, PeriodStart = DateTime.UtcNow, PeriodEnd = DateTime.UtcNow,
        };
        _t.E.Db.ProductionCycleRecords.Add(record);
        _t.E.Db.SaveChanges();
        _t.E.Db.ChangeTracker.Clear();
        var service = new ProductionCycleRecordService(_t.E.Db);

        Assert.Equal("Quarry", (await service.GetByIdAsync(record.Id))!.ProductionStructureName);
        var page = await service.SearchAsync(new PagedQueryDto { PageNumber = 1, PageSize = 10 });
        Assert.All(page.Items, i => Assert.Equal("Quarry", i.ProductionStructureName));
    }

    [Fact]
    public void TheUnitTypes_AreReadFromTheKindsSurveyProfile()
    {
        Assert.Equal(new[] { "TREE", "SAPLING" }, ProductionAreaService.UnitTypes("""{"unitTypes":["TREE","SAPLING"]}"""));
        Assert.Null(ProductionAreaService.UnitTypes(null));
        Assert.Null(ProductionAreaService.UnitTypes("""{"other":1}"""));
        Assert.Null(ProductionAreaService.UnitTypes("not json"));
    }
}

/// <summary>#875 on the survey queue: each overlap carries the unit types its structure's kind counts.</summary>
public class SurveyOverlapUnitTypesTests : IDisposable
{
    private readonly ProductionYieldTestDb _y = new();

    public void Dispose() => _y.Dispose();

    [Fact]
    public async Task TheSurveyQueuesOverlaps_CarryTheirKindsUnitTypes()
    {
        int a = await _y.LoggerAsync("Logger A");
        int farm = (await _y.Structures.CreateAsync(new ProductionStructureDto
        {
            Name = "Farm", Description = "", WgRegionId = "tmp_farm", TownId = ProductionWorldTestDb.Phoenix, ProductionKindId = 1,
        })).Id!.Value;
        var forest = await _y.RadiusAsync(a, x: 1000);
        // A shared (non-exclusive) area of the farm's kind overlapping the forest.
        await _y.Areas.CreateAsync(new ProductionAreaDto
        {
            ProductionStructureId = farm, Name = "Orchard", Shape = "Radius", Radius = 10, CenterX = 1005, CenterY = 64, CenterZ = 0, Exclusive = false,
        });

        var due = (await _y.Areas.SurveyDueAsync(50)).Single(d => d.Area.Id == forest.Id);
        var overlap = Assert.Single(due.OverlappingSharedAreas);
        Assert.Equal(new[] { "CROP" }, overlap.UnitTypes);
        Assert.Equal(new[] { "CROP" }, (await _y.Areas.OverlapsAsync(forest.Id)).Single().UnitTypes);
    }
}
