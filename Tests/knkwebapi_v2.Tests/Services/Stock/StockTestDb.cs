using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Json;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Stock;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Tests.Services.Stock;

/// <summary>
/// An EF InMemory database with the stock smoke guide's data (production smoke § 2.0): town Phoenix with two domain
/// storages S1 (cap 100) and S2 (cap 50), a player and a clan, oak logs (weight 1), stone (weight 2) and an enchanted
/// sword (a unique item). The five system storages are created on demand by the stock service.
/// </summary>
internal static class StockTestDb
{
    public const int Phoenix = 10, PlayerId = 20, ClanId = 30, BannerId = 31;
    public const int S1 = 100, S2 = 101, Vault = 102, ClanVault = 103;
    public const int OakLog = 200, Stone = 201, Sword = 202;
    public const long SwordInstance = 300;

    public static KnKDbContext Create(bool productionEnabled = true)
    {
        var db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.GameSettings.Add(new GameSettings
        {
            ProductionSettingsJson = JsonColumn.Serialize(new ProductionSettingsDto { Enabled = productionEnabled }),
        });
        db.Towns.Add(new Town { Id = Phoenix, Name = "Phoenix", Description = "t", WgRegionId = "domain_10", WorldName = "world" });
        db.Users.Add(new User { Id = PlayerId, Username = "Steve" });
        db.BannerDesigns.Add(new BannerDesign { Id = BannerId, Name = "Red" });
        db.Clans.Add(new Clan { Id = ClanId, Name = "Redcloaks", BannerDesignId = BannerId });
        db.ItemBlueprints.Add(new ItemBlueprint { Id = OakLog, Name = "Oak log", DefaultDisplayName = "Oak log" });
        db.ItemBlueprints.Add(new ItemBlueprint { Id = Stone, Name = "Stone", DefaultDisplayName = "Stone", StorageWeight = 2m });
        db.ItemBlueprints.Add(new ItemBlueprint { Id = Sword, Name = "Sword", DefaultDisplayName = "Sword", MaxStackSize = 1 });
        db.ItemInstances.Add(new ItemInstance { Id = SwordInstance, ItemBlueprintId = Sword, CustomDisplayName = "Dawnbreaker" });
        db.Storages.Add(new Storage { Id = S1, Name = "S1", Kind = StorageKind.Domain, DomainId = Phoenix, MaxUnits = 100 });
        db.Storages.Add(new Storage { Id = S2, Name = "S2", Kind = StorageKind.Domain, DomainId = Phoenix, MaxUnits = 50 });
        db.Storages.Add(new Storage { Id = Vault, Name = "Vault", Kind = StorageKind.Player, UserId = PlayerId, Purpose = StoragePurpose.Vault });
        db.Storages.Add(new Storage { Id = ClanVault, Name = "Clan vault", Kind = StorageKind.Clan, ClanId = ClanId, Purpose = StoragePurpose.Vault });
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return db;
    }

    public static StockContext Ctx(StockReason reason = StockReason.Distribution, string? key = null) =>
        new() { Reason = reason, ActorKind = StockActorKind.User, ActorUserId = PlayerId, ActorComponent = "test", IdempotencyKey = key };

    public static StockContext Adjust(string note = "count") =>
        new() { Reason = StockReason.AdminAdjust, ActorKind = StockActorKind.User, ActorUserId = PlayerId, Note = note };

    public static long Amount(KnKDbContext db, int storage, int blueprint) =>
        db.StockLines.AsNoTracking().Where(l => l.StorageId == storage && l.ItemBlueprintId == blueprint).Select(l => l.Amount)
            .FirstOrDefault();

    public static Storage Storage(KnKDbContext db, int id) => db.Storages.AsNoTracking().Single(s => s.Id == id);
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}
