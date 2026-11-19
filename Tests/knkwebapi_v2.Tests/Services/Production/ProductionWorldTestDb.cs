using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Json;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Stock;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace knkwebapi_v2.Tests.Services.Production;

/// <summary>
/// An EF InMemory database with the P2 smoke guide's data (production smoke § 3.0): town Phoenix with district Market
/// (region domain_21) and street Main, and town Rivertown. Builds the subtype services the way DI does.
/// </summary>
internal static class ProductionWorldTestDb
{
    public const int Phoenix = 10, Rivertown = 11, Market = 21, Main = 1;

    public static KnKDbContext Create(bool productionEnabled = true)
    {
        var db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        Seed(db, productionEnabled);
        return db;
    }

    public static void Seed(KnKDbContext db, bool productionEnabled)
    {
        db.GameSettings.Add(new GameSettings
        {
            ProductionSettingsJson = JsonColumn.Serialize(new ProductionSettingsDto { Enabled = productionEnabled }),
        });
        db.Towns.Add(new Town { Id = Phoenix, Name = "Phoenix", Description = "t", WgRegionId = "domain_10", WorldName = "world" });
        db.Towns.Add(new Town { Id = Rivertown, Name = "Rivertown", Description = "t", WgRegionId = "domain_11", WorldName = "world" });
        db.Districts.Add(new District { Id = Market, Name = "Market", Description = "d", WgRegionId = "domain_21", WorldName = "world", TownId = Phoenix });
        db.Streets.Add(new Street { Id = Main, Name = "Main" });
        db.SaveChanges();
        db.ChangeTracker.Clear();
    }

    public static IMapper Mapper() => new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<StructureMappingProfile>();
        cfg.AddProfile<ProductionWorldMappingProfile>();
        cfg.AddProfile<LocationMappingProfile>();
        cfg.AddProfile<DomainMappingProfile>();
    }).CreateMapper();

    public static StructureBaseWriter Writer(KnKDbContext db, ISavedEntityRuleValidator? rules = null) =>
        new(db, new StructurePlacement(db, rules), new Mock<ILocationService>().Object,
            new Mock<ITeleportDestinationService>().Object, new Mock<IDomainRegionNameFinalizer>().Object,
            FixedWorldResolver.Instance);

    public static WarehouseService Warehouses(KnKDbContext db, ISavedEntityRuleValidator? rules = null) =>
        new(db, Writer(db, rules), new StorageService(db), Mapper());

    public static ProductionStructureService Producers(KnKDbContext db, ISavedEntityRuleValidator? rules = null) =>
        new(db, Writer(db, rules), new StorageService(db), Mapper());

    public static WarehouseDto InMarket(string name = "Market warehouse") => new()
    {
        Name = name, Description = "", WgRegionId = "tmp_wh", DistrictId = Market, StreetId = Main, HouseNumber = 3,
    };

    public static ProductionStructureDto Rural(string name = "Quarry", int townId = Phoenix) => new()
    {
        Name = name, Description = "", WgRegionId = "tmp_q", TownId = townId,
    };
}
