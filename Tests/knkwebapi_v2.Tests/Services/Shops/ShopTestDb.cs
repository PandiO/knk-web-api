using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Shops;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Services.Territory;
using knkwebapi_v2.Tests.Services.Logistics;
using knkwebapi_v2.Tests.Services.Production;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static knkwebapi_v2.Tests.Services.Production.ProductionEngineTestDb;

namespace knkwebapi_v2.Tests.Services.Shops;

/// <summary>
/// The shops smoke guide's data (§ 11.0) on top of the logistics data (§ 10.0): categories Food and Weapons (Swords a
/// sub-category of Weapons); blueprints coal ("flour", base 4-6 → 5, Food), iron ore ("bread", base 18-22 → 20, Food),
/// diamond ("sword", base 90-110 → 100, Swords) and cobblestone (no base price); shop types Bakery (Processing, buys Food,
/// levels 1 and 2) and Weaponsmith (Trading, buys Weapons); the players Ann (1000 coins) and Bob (50 coins).
/// </summary>
internal sealed class ShopTestDb : IDisposable
{
    public const int Food = 900, Weapons = 901, Swords = 902;
    public const int Flour = Coal, Bread = IronOre, Sword = Diamond;

    public LogisticsTestDb L { get; }
    public ProductionEngineTestDb E => L.E;
    public CurrencyService Currency { get; }
    public ShopTypeService Types { get; }
    public ShopService Shops { get; }
    public ShopCatalogueService Catalogue { get; }
    public ShopTradingService Trading { get; private set; }
    public ShopTradeStatService Stats { get; }
    public TownAccountService Accounts { get; }
    public ItemDeliveryService Delivery { get; }

    public int Ann { get; private set; }
    public int Bob { get; private set; }
    /// <summary>Funds the town account in tests (a rich buyer).</summary>
    public int Bank { get; private set; }
    public int Bakery { get; private set; }
    public int Weaponsmith { get; private set; }

    public ShopTestDb(bool productionEnabled = true)
    {
        L = new LogisticsTestDb(productionEnabled);
        Currency = new CurrencyService(new CurrencyRepository(E.Db), new UserRepository(E.Db), NullLogger<CurrencyService>.Instance);
        Types = new ShopTypeService(E.Db);
        Shops = new ShopService(E.Db, ProductionWorldTestDb.Writer(E.Db), new StorageService(E.Db), ProductionWorldTestDb.Mapper(), E.Engine);
        Catalogue = new ShopCatalogueService(E.Db);
        Delivery = new ItemDeliveryService(E.Db, E.Stock, new ProductionSettingsReader(E.Db), E.Notifications, E.Clock);
        Trading = new ShopTradingService(E.Db, E.Stock, Currency, new ProductionSettingsReader(E.Db), Delivery);
        Stats = new ShopTradeStatService(E.Db, new ProductionSettingsReader(E.Db), E.Clock);
        Accounts = new TownAccountService(E.Db);
    }

    public void Dispose() => L.Dispose();

    public async Task<ShopTestDb> SetUpAsync(bool logistics = false)
    {
        if (logistics) await L.SetUpAsync();
        E.Db.Categories.AddRange(
            new Category { Id = Food, Name = "Food" },
            new Category { Id = Weapons, Name = "Weapons" },
            new Category { Id = Swords, Name = "Swords", ParentCategoryId = Weapons });
        E.Db.SaveChanges();
        E.Update(db => db.ItemBlueprints.Single(b => b.Id == Flour), b => { b.BasePriceMin = 4; b.BasePriceMax = 6; b.CategoryId = Food; });
        E.Update(db => db.ItemBlueprints.Single(b => b.Id == Bread), b => { b.BasePriceMin = 18; b.BasePriceMax = 22; b.CategoryId = Food; });
        E.Update(db => db.ItemBlueprints.Single(b => b.Id == Sword), b => { b.BasePriceMin = 90; b.BasePriceMax = 110; b.CategoryId = Swords; b.MaxStackSize = 1; });
        var users = new[] { new User { Username = "Ann", Coins = 1000 }, new User { Username = "Bob", Coins = 50 }, new User { Username = "Bank", Coins = 1_000_000 } };
        E.Db.Users.AddRange(users);
        E.Db.SaveChanges();
        (Ann, Bob, Bank) = (users[0].Id, users[1].Id, users[2].Id);
        E.Db.ChangeTracker.Clear();

        var bakeryType = await Types.CreateAsync(new ShopTypeDto { Name = "Bakery", DefaultKind = "Processing", AcceptedCategoryIds = new() { Food } });
        await Types.CreateLevelAsync(new ShopTypeLevelDto
        {
            ShopTypeId = bakeryType.Id, Level = 1, PriceMultiplier = 1m, MerchandiseMaxUnits = 200, InputMaxUnits = 300, ReorderPointUnits = 10, TargetUnits = 40,
        });
        await Types.CreateLevelAsync(new ShopTypeLevelDto
        {
            ShopTypeId = bakeryType.Id, Level = 2, PriceMultiplier = 1.5m, CycleSpeedMultiplier = 2m, YieldMultiplier = 1.5m, InputCostMultiplier = 0.5m,
            MerchandiseMaxUnits = 400,
        });
        var smithType = await Types.CreateAsync(new ShopTypeDto { Name = "Weaponsmith", DefaultKind = "Trading", AcceptedCategoryIds = new() { Weapons } });
        Bakery = (await Shops.CreateAsync(new ShopDto
        {
            Name = "Bakery shop", Description = "", WgRegionId = "tmp_bakeryshop", DistrictId = ProductionWorldTestDb.Market,
            StreetId = ProductionWorldTestDb.Main, HouseNumber = 9, ShopTypeId = bakeryType.Id,
        })).Id!.Value;
        Weaponsmith = (await Shops.CreateAsync(new ShopDto
        {
            Name = "Weaponsmith", Description = "", WgRegionId = "tmp_smith", TownId = ProductionWorldTestDb.Phoenix, ShopTypeId = smithType.Id,
        })).Id!.Value;
        return this;
    }

    /// <summary>A PUT as the web app sends it: the whole shop with one change.</summary>
    public async Task UpdateShopAsync(int id, Action<ShopDto> change)
    {
        var dto = (await Shops.GetByIdAsync(id))!;
        change(dto);
        await Shops.UpdateAsync(id, dto);
    }

    public int Merchandise(int shop) => E.Db.Storages.AsNoTracking().Single(s => s.DomainId == shop && s.Purpose == StoragePurpose.Merchandise).Id;

    public int Input(int shop) => E.Db.Storages.AsNoTracking().Single(s => s.DomainId == shop && s.Purpose == StoragePurpose.Input).Id;

    public long Coins(int user) => E.Db.Users.AsNoTracking().Single(u => u.Id == user).Coins;

    public long TownCoins(int town = ProductionWorldTestDb.Phoenix) =>
        E.Db.Set<TownAccount>().AsNoTracking().Where(a => a.TownId == town).Select(a => a.Coins).FirstOrDefault();

    public Task<StockResult> Put(int storage, int blueprint, long amount) => E.AdjustAsync(storage, blueprint, amount);

    public Task<ShopCatalogueEntryDto> Entry(int shop, int blueprint, long? price = null, string? mode = null) =>
        Catalogue.CreateAsync(new ShopCatalogueEntryDto { ShopId = shop, ItemBlueprintId = blueprint, StaticPrice = price, StockMode = mode });

    public static ShopPurchaseRequestDto Buy(int user, int blueprint, long amount, long? price, string key, bool confirmed = true) => new()
    {
        UserId = user, ItemBlueprintId = blueprint, Amount = amount, ExpectedUnitPrice = price, Confirmed = confirmed, IdempotencyKey = key,
    };

    /// <summary>Gives the town account coins the way the game does: a purchase (Ann buys flour from an unlimited entry).</summary>
    public async Task FundTownAsync(long coins)
    {
        var entry = await Entry(Weaponsmith, Flour, price: 1, mode: "Unlimited");
        await Trading.PurchaseAsync(Weaponsmith, new ShopPurchaseRequestDto
        {
            UserId = Bank, CatalogueEntryId = entry.Id, Amount = coins, ExpectedUnitPrice = 1, Confirmed = true, IdempotencyKey = "fund-" + coins,
        });
        await Catalogue.DeleteAsync(entry.Id!.Value);
    }

    /// <summary>A unique item the player holds (no storage), with enchantments (definition id, level).</summary>
    public long Instance(int blueprint, params (int Level, string Key)[] enchantments)
    {
        var instance = new ItemInstance { ItemBlueprintId = blueprint };
        E.Db.ItemInstances.Add(instance);
        E.Db.SaveChanges();
        foreach (var (level, key) in enchantments)
        {
            var def = E.Db.Set<EnchantmentDefinition>().FirstOrDefault(d => d.Key == key)
                ?? E.Db.Set<EnchantmentDefinition>().Add(new EnchantmentDefinition { Key = key, DisplayName = key, MaxLevel = 5 }).Entity;
            E.Db.SaveChanges();
            E.Db.Set<ItemInstanceEnchantment>().Add(new ItemInstanceEnchantment { ItemInstanceId = instance.Id, EnchantmentDefinitionId = def.Id, Level = level });
        }
        E.Db.SaveChanges();
        E.Db.ChangeTracker.Clear();
        return instance.Id;
    }
}
