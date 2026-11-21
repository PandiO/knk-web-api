using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Json;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Shops;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Services.Territory;
using knkwebapi_v2.Tests.Services.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// Shops P10 (PandiO/knk-workspace#248) on MySQL: two players racing for the last units of a shop. Both pass the checks
/// and pay; the second's goods are gone, so its whole purchase rolls back: one player is charged, the town account holds
/// exactly the one payment and agrees with its last ledger leg, and the stock left is right (A18, "nothing is taken
/// without the item").
/// </summary>
[Trait("Category", "requires-mysql")]
public class ShopsMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public ShopsMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static ShopTradingService Trading(KnKDbContext ctx, TimeProvider clock) =>
        new(ctx, new StockService(ctx, clock),
            new CurrencyService(new CurrencyRepository(ctx), new UserRepository(ctx), NullLogger<CurrencyService>.Instance),
            new ProductionSettingsReader(ctx));

    [MySqlFact]
    public async Task RacingPurchasesOfTheLastUnits_ChargeOnlyTheOneThatGotThem()
    {
        var clock = new TestClock { Now = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero) };
        var suffix = Guid.NewGuid().ToString("N")[..10];
        int townId, shopId, merch, bread;
        int[] users;
        await using (var ctx = _db.NewContext())
        {
            var settings = await ctx.GameSettings.FirstOrDefaultAsync();
            if (settings == null) { settings = new GameSettings(); ctx.GameSettings.Add(settings); }
            settings.ProductionSettingsJson = JsonColumn.Serialize(new ProductionSettingsDto { Enabled = true });
            var town = new Town { Name = "T " + suffix, Description = "t", WgRegionId = "tt_" + suffix, WorldName = "world" };
            ctx.Towns.Add(town);
            var bp = new ItemBlueprint { Name = "Bread " + suffix, DefaultDisplayName = "Bread", BasePriceMin = 18, BasePriceMax = 22 };
            ctx.ItemBlueprints.Add(bp);
            var players = new[] { new User { Username = "a" + suffix, Coins = 100 }, new User { Username = "b" + suffix, Coins = 100 } };
            ctx.Users.AddRange(players);
            await ctx.SaveChangesAsync();
            var shop = new Shop { Name = "S " + suffix, Description = "", WgRegionId = "s_" + suffix, WorldName = "world", TownId = town.Id };
            ctx.Set<Shop>().Add(shop);
            await ctx.SaveChangesAsync();
            var m = new Storage { Name = "m " + suffix, Kind = StorageKind.Domain, DomainId = shop.Id, Purpose = StoragePurpose.Merchandise };
            ctx.Storages.Add(m);
            ctx.Set<ShopCatalogueEntry>().Add(new ShopCatalogueEntry { ShopId = shop.Id, ItemBlueprintId = bp.Id });
            await ctx.SaveChangesAsync();
            (townId, shopId, merch, bread, users) = (town.Id, shop.Id, m.Id, bp.Id, players.Select(p => p.Id).ToArray());
            // Balances are written by the ledger only (PropertySaveBehavior.Ignore): set them directly for the test.
            await ctx.Users.Where(u => users.Contains(u.Id)).ExecuteUpdateAsync(x => x.SetProperty(u => u.Coins, 100));
            await new StockService(ctx, clock).AdjustAsync(m.Id, bp.Id, null, 3,
                new StockContext { Reason = StockReason.AdminAdjust, ActorComponent = "test", Note = "test" });
        }

        var outcomes = await Task.WhenAll(users.Select(async (u, i) =>
        {
            await using var ctx = _db.NewContext();
            try
            {
                await Trading(ctx, clock).PurchaseAsync(shopId, new ShopPurchaseRequestDto
                {
                    UserId = u, ItemBlueprintId = bread, Amount = 2, ExpectedUnitPrice = 20, Confirmed = true, IdempotencyKey = $"race-{suffix}-{i}",
                });
                return true;
            }
            catch (ShopRefusedException ex) when (ex.Code is "NotEnoughStock")
            {
                return false;
            }
        }));

        Assert.Equal(1, outcomes.Count(o => o));
        await using (var ctx = _db.NewContext())
        {
            var coins = await ctx.Users.AsNoTracking().Where(u => users.Contains(u.Id)).Select(u => u.Coins).ToListAsync();
            Assert.Equal(new[] { 60, 100 }, coins.OrderBy(c => c).ToArray());
            var account = await ctx.Set<TownAccount>().AsNoTracking().SingleAsync(a => a.TownId == townId);
            Assert.Equal(40, account.Coins);
            var legs = await ctx.CurrencyEntries.AsNoTracking().Where(e => e.AccountKind == CurrencyAccountKind.Town && e.TownId == townId).ToListAsync();
            Assert.Equal(40L, Assert.Single(legs).BalanceAfter);
            Assert.Equal(1, await ctx.StockLines.AsNoTracking().Where(l => l.StorageId == merch && l.ItemBlueprintId == bread).Select(l => l.Amount).SingleAsync());
            Assert.Equal(1, await ctx.StockTransactions.CountAsync(t => t.Reason == StockReason.ShopSale && t.SourceRef == shopId.ToString()));
        }
    }

    [MySqlFact]
    public async Task APurchaseWhoseGoodsCantBeTaken_RollsBackThePayment()
    {
        var clock = new TestClock { Now = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero) };
        var suffix = Guid.NewGuid().ToString("N")[..10];
        int townId, shopId, bread, user;
        await using (var ctx = _db.NewContext())
        {
            var town = new Town { Name = "T " + suffix, Description = "t", WgRegionId = "tt_" + suffix, WorldName = "world" };
            ctx.Towns.Add(town);
            var bp = new ItemBlueprint { Name = "Bread " + suffix, DefaultDisplayName = "Bread", BasePriceMin = 18, BasePriceMax = 22 };
            ctx.ItemBlueprints.Add(bp);
            var player = new User { Username = "c" + suffix };
            ctx.Users.Add(player);
            await ctx.SaveChangesAsync();
            var shop = new Shop { Name = "S " + suffix, Description = "", WgRegionId = "s_" + suffix, WorldName = "world", TownId = town.Id };
            ctx.Set<Shop>().Add(shop);
            await ctx.SaveChangesAsync();
            var m = new Storage { Name = "m " + suffix, Kind = StorageKind.Domain, DomainId = shop.Id, Purpose = StoragePurpose.Merchandise };
            ctx.Storages.Add(m);
            ctx.Set<ShopCatalogueEntry>().Add(new ShopCatalogueEntry { ShopId = shop.Id, ItemBlueprintId = bp.Id });
            await ctx.SaveChangesAsync();
            (townId, shopId, bread, user) = (town.Id, shop.Id, bp.Id, player.Id);
            await ctx.Users.Where(u => u.Id == user).ExecuteUpdateAsync(x => x.SetProperty(u => u.Coins, 100));
            await new StockService(ctx, clock).AdjustAsync(m.Id, bp.Id, null, 5,
                new StockContext { Reason = StockReason.AdminAdjust, ActorComponent = "test", Note = "test" });
            // Locked after the stock arrived: the checks see 5 in stock, the move is refused after the coins were posted.
            await ctx.Storages.Where(s => s.Id == m.Id).ExecuteUpdateAsync(x => x
                .SetProperty(s => s.LockedUntil, clock.Now.UtcDateTime.AddDays(1)).SetProperty(s => s.LockReason, "investigation"));
        }

        await using (var ctx = _db.NewContext())
        {
            var ex = await Assert.ThrowsAsync<ShopRefusedException>(() => Trading(ctx, clock).PurchaseAsync(shopId, new ShopPurchaseRequestDto
            {
                UserId = user, ItemBlueprintId = bread, Amount = 2, ExpectedUnitPrice = 20, Confirmed = true, IdempotencyKey = "locked-" + suffix,
            }));
            Assert.Equal("StorageLocked", ex.Code);
        }

        await using (var ctx = _db.NewContext())
        {
            Assert.Equal(100, await ctx.Users.AsNoTracking().Where(u => u.Id == user).Select(u => u.Coins).SingleAsync());
            Assert.Equal(0, await ctx.Set<TownAccount>().AsNoTracking().Where(a => a.TownId == townId).Select(a => a.Coins).SingleOrDefaultAsync());
            Assert.False(await ctx.CurrencyTransactions.AnyAsync(t => t.IdempotencyKey == "shop-buy:locked-" + suffix));
        }
    }
}
