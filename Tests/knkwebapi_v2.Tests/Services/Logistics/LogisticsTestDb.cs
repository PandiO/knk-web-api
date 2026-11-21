using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Logistics;
using knkwebapi_v2.Services.Territory;
using knkwebapi_v2.Tests.Services.Production;
using Microsoft.EntityFrameworkCore;
using static knkwebapi_v2.Tests.Services.Production.ProductionEngineTestDb;

namespace knkwebapi_v2.Tests.Services.Logistics;

/// <summary>
/// The logistics smoke guide's data (§ 10.0) on top of the transport data (§ 6.0): town Phoenix with the warehouses
/// North and South, and two consumers in district Market, each with an Input storage (capped at 500) and two workers:
/// the Bakery and the Garrison. Lanes: Bakery → North 432 blocks (100 s), Bakery → South 864, Garrison → North 432,
/// Garrison → South 216. "Flour" is coal, "bread" is iron ore (the engine fixture's blueprints).
/// </summary>
internal sealed class LogisticsTestDb : IDisposable
{
    public const int Flour = Coal, Bread = IronOre;

    public TransportTestDb T { get; }
    public ProductionEngineTestDb E => T.E;
    public LogisticsPolicyService Policies { get; }
    public ConsumerReplenishmentService Settings { get; }
    public LogisticsRecordsService Records { get; private set; }
    public LogisticsPlanner Planner { get; private set; }

    public int Bakery { get; private set; }
    public int BakeryInput { get; private set; }
    public int Garrison { get; private set; }
    public int GarrisonInput { get; private set; }

    public LogisticsTestDb(bool productionEnabled = true)
    {
        T = new TransportTestDb(productionEnabled);
        Policies = new LogisticsPolicyService(E.Db, E.Clock);
        Settings = new ConsumerReplenishmentService(E.Db, E.Clock);
        Records = new LogisticsRecordsService(E.Db, T.Transport, E.Clock);
        Planner = NewPlanner();
    }

    public LogisticsPlanner NewPlanner() => new(E.Db, T.Transport, new ProductionSettingsReader(E.Db), E.Clock);

    public void Dispose() => T.Dispose();

    public async Task<LogisticsTestDb> SetUpAsync()
    {
        await T.SetUpAsync();
        Bakery = await ConsumerAsync("Bakery", 5);
        BakeryInput = InputOf(Bakery);
        Garrison = await ConsumerAsync("Garrison", 7);
        GarrisonInput = InputOf(Garrison);
        await T.Lane(Bakery, T.North, 432m);
        await T.Lane(Bakery, T.South, 864m);
        await T.Lane(Garrison, T.North, 432m);
        await T.Lane(Garrison, T.South, 216m);
        return this;
    }

    /// <summary>A production structure in district Market with an Input storage capped at 500 (no lines).</summary>
    public async Task<int> ConsumerAsync(string name, int house, int? districtId = ProductionWorldTestDb.Market)
    {
        var s = await E.Structures.CreateAsync(new ProductionStructureDto
        {
            Name = name, Description = "", WgRegionId = "tmp_" + name, DistrictId = districtId, StreetId = districtId == null ? null : ProductionWorldTestDb.Main,
            HouseNumber = districtId == null ? 0 : house, TownId = districtId == null ? ProductionWorldTestDb.Phoenix : null,
        });
        E.Db.Storages.Add(new Storage { Name = name + " inputs", Kind = StorageKind.Domain, DomainId = s.Id, Purpose = StoragePurpose.Input, MaxUnits = 500 });
        E.Db.SaveChanges();
        E.Db.ChangeTracker.Clear();
        return s.Id!.Value;
    }

    public int InputOf(int structure) =>
        E.Db.Storages.AsNoTracking().Single(s => s.DomainId == structure && s.Purpose == StoragePurpose.Input).Id;

    public Task<ConsumerReplenishmentDto> MinMax(int storage, int blueprint, long min, long max, string? priority = null) =>
        Settings.CreateAsync(new ConsumerReplenishmentDto { StorageId = storage, ItemBlueprintId = blueprint, ReorderPoint = min, TargetLevel = max, PriorityClass = priority });

    public Task<LogisticsPolicyDto> TownPolicy(params LogisticsRuleDto[] rules) =>
        Policies.CreateAsync(new LogisticsPolicyDto
        {
            Name = "Phoenix logistics", Scope = "Town", TownId = ProductionWorldTestDb.Phoenix, Rules = rules.ToList(),
        }, actorUserId: 7);

    public static LogisticsRuleDto Reserve(int blueprint, long? units = null, decimal? days = null, int? warehouse = null) => new()
    {
        Type = "Reserve", ItemBlueprintId = blueprint, Units = units, DaysOfCover = days,
        Scope = warehouse == null ? "Town" : "Warehouse", WarehouseId = warehouse, Reason = "Keep some back",
    };

    public static LogisticsRuleDto Priority(int structure, string cls) => new() { Type = "PriorityClass", StructureId = structure, PriorityClass = cls };

    public Task<LogisticsPlanDto> Run() => Planner.RunAsync(ProductionWorldTestDb.Phoenix, 7);

    public Task<LogisticsPlanDto> DryRun() => Planner.DryRunAsync(ProductionWorldTestDb.Phoenix);

    public List<LogisticsReservation> Reservations() => E.Db.Set<LogisticsReservation>().AsNoTracking().OrderBy(r => r.Id).ToList();

    public List<TransportOrder> Distribution() => E.Db.TransportOrders.AsNoTracking().Include(o => o.Lines).Include(o => o.Stops).ThenInclude(s => s.Lines)
        .Where(o => o.Origin == TransportOrigin.Distribution).OrderBy(o => o.Id).ToList();

    public static LogisticsPlanEntryDto Entry(LogisticsPlanDto plan, int storage, int blueprint) =>
        plan.Entries.Single(e => e.ConsumerStorageId == storage && e.ItemBlueprintId == blueprint);
}
