using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Json;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Services.Territory;
using knkwebapi_v2.Tests.Services.Stock;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Tests.Services.Production;

/// <summary>
/// The production smoke guide's data (§ 4.0) on an EF InMemory database: the P2 towns (Phoenix with district Market,
/// Rivertown), the seeded kind catalogue and default climate zone (EnsureCreated applies the migration's seed rows), and
/// blueprints cobblestone, coal and iron ore. <see cref="Fixture"/> builds the engine, the services and a rural quarry
/// with 60 cobblestone per hour, an Output storage capped at 100 and two workers.
/// </summary>
internal sealed class ProductionEngineTestDb : IDisposable
{
    public const int Cobblestone = 500, Coal = 501, IronOre = 502, Diamond = 503;
    public const int QuarryKind = 3, MineKind = 4;

    public KnKDbContext Db { get; }
    public TestClock Clock { get; } = new();
    public InMemoryPlayerNotificationQueue Notifications { get; } = new();
    public StockService Stock { get; }
    public ProductionEngine Engine { get; }
    public ProductionStructureService Structures { get; }
    public ProductionLineService Lines { get; }

    public ProductionEngineTestDb(bool productionEnabled = true, ICurrencyService? currency = null)
    {
        Db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        Db.Database.EnsureCreated();
        ProductionWorldTestDb.Seed(Db, productionEnabled);
        Db.ItemBlueprints.Add(new ItemBlueprint { Id = Cobblestone, Name = "Cobblestone", DefaultDisplayName = "Cobblestone" });
        Db.ItemBlueprints.Add(new ItemBlueprint { Id = Coal, Name = "Coal", DefaultDisplayName = "Coal" });
        Db.ItemBlueprints.Add(new ItemBlueprint { Id = IronOre, Name = "Iron ore", DefaultDisplayName = "Iron ore" });
        Db.ItemBlueprints.Add(new ItemBlueprint { Id = Diamond, Name = "Diamond", DefaultDisplayName = "Diamond" });
        Db.SaveChanges();
        Db.ChangeTracker.Clear();

        Stock = new StockService(Db, Clock);
        var weather = new ProductionWeatherService(Db, Clock);
        Engine = new ProductionEngine(Db, Stock, new ProductionSettingsReader(Db), weather,
            new IProductionModifierSource[] { new LevelModifierSource(), new WeatherModifierSource(Db, weather), new StoredModifierSource(Db) },
            new IProductionBaseRateProvider[] { new ConfiguredBaseRateProvider() }, currency, Notifications, Clock);
        Structures = new ProductionStructureService(Db, ProductionWorldTestDb.Writer(Db), new StorageService(Db),
            ProductionWorldTestDb.Mapper(), Engine);
        Lines = new ProductionLineService(Db, new ProductionSettingsReader(Db), Engine, Clock);
    }

    public void Dispose() => Db.Dispose();

    public void Advance(TimeSpan by) => Clock.Now += by;

    public void Advance(int minutes) => Advance(TimeSpan.FromMinutes(minutes));

    /// <summary>The smoke § 4.0 quarry: rural in Phoenix, kind Quarry, level 1, Output capped at <paramref name="cap"/>.</summary>
    public async Task<(int StructureId, int LineId, int OutputId)> QuarryAsync(decimal rate = 60m, long? cap = 100, int? kind = QuarryKind,
        string name = "Quarry")
    {
        var s = await Structures.CreateAsync(new ProductionStructureDto
        {
            Name = name, Description = "", WgRegionId = "tmp_" + name, TownId = ProductionWorldTestDb.Phoenix, ProductionKindId = kind,
        });
        int output = Db.Storages.AsNoTracking().Single(x => x.DomainId == s.Id && x.Purpose == StoragePurpose.Output).Id;
        if (cap.HasValue)
        {
            var storage = Db.Storages.Single(x => x.Id == output);
            storage.MaxUnits = cap;
            Db.SaveChanges();
            Db.ChangeTracker.Clear();
        }
        var line = await Lines.CreateAsync(new ProductionLineDto
        {
            ProductionStructureId = s.Id, ItemBlueprintId = Cobblestone, ConfiguredRatePerHour = rate,
        });
        return (s.Id!.Value, line.Id, output);
    }

    public long Amount(int storage, int blueprint) => StockTestDb.Amount(Db, storage, blueprint);

    public ProductionLine Line(int id) => Db.ProductionLines.AsNoTracking().Single(l => l.Id == id);

    public List<ProductionCycleRecord> Records(int lineId) =>
        Db.ProductionCycleRecords.AsNoTracking().Where(r => r.ProductionLineId == lineId).OrderBy(r => r.Id).ToList();

    public void SetSettings(Action<ProductionSettingsDto> change)
    {
        var row = Db.GameSettings.Single();
        var dto = ProductionSettings.Read(row);
        change(dto);
        row.ProductionSettingsJson = JsonColumn.Serialize(dto);
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    public Task<StockResult> AdjustAsync(int storage, int blueprint, long delta) =>
        Stock.AdjustAsync(storage, blueprint, null, delta, new StockContext { Reason = StockReason.AdminAdjust, ActorComponent = "test", Note = "test" });

    /// <summary>A PUT as the web app sends it: the whole structure with one change.</summary>
    public async Task UpdateStructureAsync(int id, Action<ProductionStructureDto> change)
    {
        var dto = (await Structures.GetByIdAsync(id))!;
        change(dto);
        await Structures.UpdateAsync(id, dto);
    }

    public void Update<T>(Func<KnKDbContext, T> find, Action<T> change) where T : class
    {
        var entity = find(Db);
        change(entity);
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }
}
