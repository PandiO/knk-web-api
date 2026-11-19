using knkwebapi_v2.Dtos;
using knkwebapi_v2.Json;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Tests.Services.Territory;

/// <summary>An EF InMemory database with the smoke guide's test data (§ 1.0): Phoenix (power 50, a capital district and a
/// normal one) and Rivertown (power 10) in one kingdom, ranges 100 + power × 2, capital × 1.5.</summary>
internal static class TerritoryTestDb
{
    public const int KingdomId = 1, Phoenix = 10, Rivertown = 11, PhoenixCapital = 20, PhoenixMarket = 21, RivertownCentre = 22;
    public const int Bakery = 30, Mill = 31;

    public static KnKDbContext Create(bool productionEnabled = true)
    {
        var db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.GameSettings.Add(new GameSettings
        {
            ProductionSettingsJson = JsonColumn.Serialize(new ProductionSettingsDto
            {
                Enabled = productionEnabled,
                Territory = new TerritorySettingsDto { BaseRange = 100, PowerFactor = 2, CapitalMultiplier = 1.5 },
            }),
        });
        db.Kingdoms.Add(new Kingdom { Id = KingdomId, Name = "Aurelia" });
        db.Towns.Add(new Town { Id = Phoenix, Name = "Phoenix", Description = "t", WgRegionId = "domain_10", WorldName = "world", KingdomId = KingdomId, Power = 50 });
        db.Towns.Add(new Town { Id = Rivertown, Name = "Rivertown", Description = "t", WgRegionId = "domain_11", WorldName = "world", KingdomId = KingdomId, Power = 10 });
        db.Districts.Add(new District { Id = PhoenixCapital, Name = "Old Town", Description = "d", WgRegionId = "domain_20", WorldName = "world", TownId = Phoenix, IsCapital = true });
        db.Districts.Add(new District { Id = PhoenixMarket, Name = "Market", Description = "d", WgRegionId = "domain_21", WorldName = "world", TownId = Phoenix });
        db.Districts.Add(new District { Id = RivertownCentre, Name = "Centre", Description = "d", WgRegionId = "domain_22", WorldName = "world", TownId = Rivertown });
        db.DomainSubRegions.Add(new DomainSubRegion { Id = 40, DomainId = PhoenixMarket, WgRegionId = "domain_21_sub_40" });
        db.Structures.Add(new Structure { Id = Bakery, Name = "Bakery", Description = "s", WgRegionId = "domain_30", WorldName = "world", DistrictId = PhoenixMarket, StreetId = 1 });
        db.Structures.Add(new Structure { Id = Mill, Name = "Mill", Description = "s", WgRegionId = "domain_31", WorldName = "world", DistrictId = RivertownCentre, StreetId = 1 });
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return db;
    }

    public static TerritoryHolderDto Town(int id) => new() { Kind = "Town", Id = id };

    public static TerritoryHolderDto KingdomHolder(int id) => new() { Kind = "Kingdom", Id = id };
}
