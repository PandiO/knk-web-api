using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Shops;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Tests.Services.Production;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static knkwebapi_v2.Tests.Services.Shops.ShopTestDb;
using static knkwebapi_v2.Tests.Services.Production.ProductionEngineTestDb;

namespace knkwebapi_v2.Tests.Services.Shops;

/// <summary>Shops P10 (PandiO/knk-workspace#248): the admin catalogue, buying (coins to the town account), interim
/// pricing, selling to shops and the town account (smoke guide § 11.1).</summary>
public class ShopTradingTests : IDisposable
{
    private readonly ShopTestDb _s = new();
    private ProductionEngineTestDb E => _s.E;

    public void Dispose() => _s.Dispose();

    // ================= Pricing =================

    [Theory]
    [InlineData(4, 6, 5)]
    [InlineData(6, 4, 5)]
    [InlineData(0, 0, 0)]
    [InlineData(-3, 9, 4.5)]
    public void BasePrice_IsTheMidpoint(decimal min, decimal max, decimal expected) =>
        Assert.Equal(expected, ShopPricing.BasePrice(min, max));

    [Fact]
    public void SellPrice_StaticWins_ElseFormulaRounded_AtLeastOne()
    {
        Assert.Equal(new ShopPrice(7, ShopPriceSource.Static), ShopPricing.SellPrice(7, 100m, 2m, 2m));
        Assert.Equal(new ShopPrice(30, ShopPriceSource.Formula), ShopPricing.SellPrice(null, 20m, 1.25m, 1.2m));
        Assert.Equal(new ShopPrice(3, ShopPriceSource.Formula), ShopPricing.SellPrice(null, 2.5m, 1m, 1m)); // half away from zero
        Assert.Equal(new ShopPrice(1, ShopPriceSource.Formula), ShopPricing.SellPrice(null, 0.1m, 1m, 1m));
        Assert.Equal(new ShopPrice(null, ShopPriceSource.None), ShopPricing.SellPrice(null, 0m, 1m, 1m));
        Assert.Equal(new ShopPrice(130, ShopPriceSource.Formula),
            ShopPricing.SellPrice(null, 100m, 1m, 1m, ShopPricing.EnchantmentFactor(new[] { 2, 1 }, 10m)));
        Assert.Equal(12, ShopPricing.BuyBack(25, 50m));
        Assert.Equal(0, ShopPricing.BuyBack(1, 50m));
    }

    // ================= Shops and catalogue =================

    [Fact]
    public async Task AShop_GetsMerchandiseAndInputStorages_CappedByItsLevel_AndItsOwnWorker()
    {
        await _s.SetUpAsync();
        var shop = (await _s.Shops.GetByIdAsync(_s.Bakery))!;
        Assert.Equal(("Processing", 1, 1, 1), (shop.Kind, shop.Level, shop.WorkersTotal, shop.WorkersPresent));
        Assert.Equal(new[] { "Merchandise", "Input" }, shop.Storages!.Select(s => s.Purpose).ToArray());
        Assert.Equal(ProductionWorldTestDb.Phoenix, shop.EffectiveTownId);
        var merch = E.Db.Storages.AsNoTracking().Single(s => s.Id == _s.Merchandise(_s.Bakery));
        Assert.Equal(200, merch.MaxUnits);
        var p = E.Db.ProductionStructures.AsNoTracking().Single(x => x.Id == _s.Bakery);
        Assert.False(p.AutomaticTransport);
        Assert.Equal(ProductionRateSource.Configured, p.YieldSource);
        Assert.Equal("Trading", (await _s.Shops.GetByIdAsync(_s.Weaponsmith))!.Kind);

        await _s.UpdateShopAsync(_s.Bakery, d => d.Level = 2);
        Assert.Equal(400, E.Db.Storages.AsNoTracking().Single(s => s.Id == _s.Merchandise(_s.Bakery)).MaxUnits);
    }

    [Fact]
    public async Task TheCatalogue_ListsABlueprintOnce_AndAUniqueItemOnce()
    {
        await _s.SetUpAsync();
        await _s.Entry(_s.Bakery, Bread);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _s.Entry(_s.Bakery, Bread));
        long sword = _s.Instance(Sword);
        await _s.Catalogue.CreateAsync(new ShopCatalogueEntryDto { ShopId = _s.Weaponsmith, ItemInstanceId = sword, StaticPrice = 500 });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _s.Catalogue.CreateAsync(new ShopCatalogueEntryDto { ShopId = _s.Bakery, ItemInstanceId = sword }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _s.Catalogue.CreateAsync(new ShopCatalogueEntryDto { ShopId = _s.Bakery, ItemInstanceId = _s.Instance(Sword), StockMode = "Unlimited" }));
    }

    [Fact]
    public async Task TheOffer_ShowsEntriesAndMerchandise_WithPriceAndStock_SoldOutGreyedOrHidden()
    {
        await _s.SetUpAsync();
        await _s.Entry(_s.Bakery, Bread);                // formula 20
        await _s.Entry(_s.Bakery, Flour, price: 9);      // static 9
        await _s.Put(_s.Merchandise(_s.Bakery), Bread, 5);
        await _s.Put(_s.Merchandise(_s.Bakery), Sword, 1); // stock without an entry: formula 100

        var offer = await _s.Trading.OfferAsync(_s.Bakery);
        Assert.Equal(("Phoenix", false), (offer.TownName, offer.HideSoldOut));
        Assert.True(offer.BuysFromPlayers);
        var bread = offer.Items.Single(i => i.ItemBlueprintId == Bread);
        Assert.Equal((20L, "Formula", 5L, false), (bread.UnitPrice!.Value, bread.PriceSource, bread.Stock!.Value, bread.SoldOut));
        var flour = offer.Items.Single(i => i.ItemBlueprintId == Flour);
        Assert.Equal((9L, "Static", true), (flour.UnitPrice!.Value, flour.PriceSource, flour.SoldOut));
        Assert.Equal(100, offer.Items.Single(i => i.ItemBlueprintId == Sword).UnitPrice);

        await _s.UpdateShopAsync(_s.Bakery, d => d.HideSoldOut = true);
        Assert.DoesNotContain((await _s.Trading.OfferAsync(_s.Bakery)).Items, i => i.ItemBlueprintId == Flour);
    }

    // ================= Buying =================

    [Fact]
    public async Task APurchase_MovesCoinsToTheTownAccount_AndTheGoodsOutOfMerchandise_Together()
    {
        await _s.SetUpAsync();
        await _s.Entry(_s.Bakery, Bread);
        await _s.Put(_s.Merchandise(_s.Bakery), Bread, 10);

        var quote = await _s.Trading.QuoteAsync(_s.Bakery, Buy(_s.Ann, Bread, 3, null, "q"));
        Assert.Equal((20L, 60L, (string?)null), (quote.UnitPrice, quote.TotalPrice, quote.Refusal));
        var result = await _s.Trading.PurchaseAsync(_s.Bakery, Buy(_s.Ann, Bread, 3, 20, "buy-1"));

        Assert.Equal((60L, "Inventory", ProductionWorldTestDb.Phoenix, 940L), (result.TotalPrice, result.DeliverTo, result.TownId, result.CoinsAfter!.Value));
        Assert.Equal(940, _s.Coins(_s.Ann));
        Assert.Equal(60, _s.TownCoins());
        Assert.Equal(7, E.Amount(_s.Merchandise(_s.Bakery), Bread));
        var tx = E.Db.StockTransactions.AsNoTracking().Include(t => t.Moves).Single(t => t.Id == result.StockTransactionId);
        Assert.Equal((StockReason.ShopSale, "Shop", _s.Bakery.ToString(), _s.Ann), (tx.Reason, tx.SourceType, tx.SourceRef, tx.ActorUserId!.Value));
        var world = await E.Stock.GetSystemStorageIdAsync(Storage.SysWorld);
        Assert.Equal((_s.Merchandise(_s.Bakery), world, 3L), (tx.Moves.Single().FromStorageId, tx.Moves.Single().ToStorageId, tx.Moves.Single().Amount));

        var legs = E.Db.CurrencyEntries.AsNoTracking().Where(e => e.Transaction.PublicId == result.CurrencyTransactionId).OrderBy(e => e.Id).ToList();
        Assert.Equal(2, legs.Count);
        Assert.Equal((CurrencyAccountKind.User, -60L), (legs[0].AccountKind, legs[0].Amount));
        Assert.Equal((CurrencyAccountKind.Town, (int?)ProductionWorldTestDb.Phoenix, 60L, 0L, 60L),
            (legs[1].AccountKind, legs[1].TownId, legs[1].Amount, legs[1].BalanceBefore!.Value, legs[1].BalanceAfter!.Value));
        var account = (await _s.Accounts.GetByTownAsync(ProductionWorldTestDb.Phoenix))!;
        Assert.True(account.Reconciled);
        Assert.Equal(CurrencyReasons.ShopPurchase, (await _s.Accounts.LedgerAsync(account.Id, new PagedQueryDto())).Items.Single().ReasonCode);
    }

    [Fact]
    public async Task APurchase_IsIdempotent_OnItsKey()
    {
        await _s.SetUpAsync();
        await _s.Entry(_s.Bakery, Bread);
        await _s.Put(_s.Merchandise(_s.Bakery), Bread, 10);
        var first = await _s.Trading.PurchaseAsync(_s.Bakery, Buy(_s.Ann, Bread, 2, 20, "same"));
        var again = await _s.Trading.PurchaseAsync(_s.Bakery, Buy(_s.Ann, Bread, 2, 20, "same"));
        Assert.True(again.Replayed);
        Assert.Equal((first.StockTransactionId, first.CurrencyTransactionId, 40L), (again.StockTransactionId, again.CurrencyTransactionId, again.TotalPrice));
        Assert.Equal(960, _s.Coins(_s.Ann));
        Assert.Equal(8, E.Amount(_s.Merchandise(_s.Bakery), Bread));
        var other = await Assert.ThrowsAsync<ShopRefusedException>(() => _s.Trading.PurchaseAsync(_s.Bakery, Buy(_s.Bob, Bread, 2, 20, "same")));
        Assert.Equal("IdempotencyKeyReuse", other.Code);
    }

    [Theory]
    [InlineData(false, 20L, 1L, "NotConfirmed")]
    [InlineData(true, 19L, 1L, "PriceChanged")]
    [InlineData(true, 20L, 11L, "NotEnoughStock")]
    [InlineData(true, 20L, 3L, "InsufficientFunds")] // Bob has 50 coins
    public async Task APurchase_IsRefused_WithoutTakingAnything(bool confirmed, long price, long amount, string code)
    {
        await _s.SetUpAsync();
        await _s.Entry(_s.Bakery, Bread);
        await _s.Put(_s.Merchandise(_s.Bakery), Bread, 10);
        var ex = await Assert.ThrowsAsync<ShopRefusedException>(() =>
            _s.Trading.PurchaseAsync(_s.Bakery, Buy(_s.Bob, Bread, amount, price, "r-" + code, confirmed)));
        Assert.Equal(code, ex.Code);
        Assert.Equal(50, _s.Coins(_s.Bob));
        Assert.Equal(0, _s.TownCoins());
        Assert.Equal(10, E.Amount(_s.Merchandise(_s.Bakery), Bread));
    }

    [Fact]
    public async Task ASoldOutItem_AndAnItemWithoutPrice_AreRefused()
    {
        await _s.SetUpAsync();
        await _s.Entry(_s.Bakery, Bread);
        Assert.Equal("SoldOut", (await _s.Trading.QuoteAsync(_s.Bakery, Buy(_s.Ann, Bread, 1, null, "x"))).Refusal);
        await _s.Entry(_s.Bakery, Cobblestone); // no base price, no static price
        Assert.Equal("NotForSale", (await _s.Trading.QuoteAsync(_s.Bakery, Buy(_s.Ann, Cobblestone, 1, null, "x"))).Refusal);
        Assert.Equal("NotForSale", (await _s.Trading.QuoteAsync(_s.Bakery, Buy(_s.Ann, Sword, 1, null, "x"))).Refusal);
    }

    [Fact]
    public async Task AStaticPrice_Wins_AndTheLevelAndTaxModifiersApplyToTheFormula()
    {
        await _s.SetUpAsync();
        await _s.Entry(_s.Bakery, Bread);
        await _s.Entry(_s.Bakery, Flour, price: 9);
        await _s.UpdateShopAsync(_s.Bakery, d => { d.Level = 2; d.TaxModifier = 1.1m; });
        var offer = await _s.Trading.OfferAsync(_s.Bakery);
        Assert.Equal(33, offer.Items.Single(i => i.ItemBlueprintId == Bread).UnitPrice); // 20 × 1.1 × 1.5
        Assert.Equal(9, offer.Items.Single(i => i.ItemBlueprintId == Flour).UnitPrice);
    }

    [Fact]
    public async Task AnUnlimitedEntry_NeverSellsOut_AndWorksWhileProductionIsOff()
    {
        using var off = new ShopTestDb(productionEnabled: false);
        await off.SetUpAsync();
        await off.Entry(off.Bakery, Bread, price: 15, mode: "Unlimited");
        var r = await off.Trading.PurchaseAsync(off.Bakery, Buy(off.Ann, Bread, 4, 15, "u"));
        Assert.Equal(60, r.TotalPrice);
        Assert.Equal(60, off.TownCoins());
        var adjust = await off.E.Stock.GetSystemStorageIdAsync(Storage.SysAdjustment);
        Assert.Equal(adjust, off.E.Db.StockMoves.AsNoTracking().Single(m => m.TransactionId == r.StockTransactionId).FromStorageId);
        var inbox = await Assert.ThrowsAsync<ProductionOffException>(() =>
            off.Trading.PurchaseAsync(off.Bakery, Buy(off.Ann, Bread, 1, 15, "u2").WithInbox()));
        Assert.Contains("Production:Enabled", inbox.Message);
    }

    [Fact]
    public async Task APurchaseToTheInbox_DeliversIt_ThroughP7()
    {
        await _s.SetUpAsync();
        await _s.Entry(_s.Bakery, Bread);
        await _s.Put(_s.Merchandise(_s.Bakery), Bread, 10);
        var r = await _s.Trading.PurchaseAsync(_s.Bakery, Buy(_s.Ann, Bread, 2, 20, "inbox").WithInbox());
        Assert.NotNull(r.ItemDeliveryId);
        var view = await _s.Delivery.ViewAsync(_s.Ann, StoragePurpose.Inbox);
        Assert.Equal(2, E.Amount(view.StorageId!.Value, Bread));
        Assert.Equal(8, E.Amount(_s.Merchandise(_s.Bakery), Bread));
    }

    // ================= Selling =================

    [Fact]
    public async Task SellingToAShop_PaysHalfTheSellPrice_FromTheTownAccount()
    {
        await _s.SetUpAsync();
        await _s.FundTownAsync(500);
        var request = new ShopSellRequestDto { UserId = _s.Bob, Lines = new() { new ShopSellLineDto { ItemBlueprintId = Bread, Amount = 4 } }, IdempotencyKey = "sell-1" };
        var quote = await _s.Trading.SellQuoteAsync(_s.Bakery, request);
        Assert.Equal((10L, 40L, true, true), (quote.Lines[0].UnitPrice, quote.Total, quote.Accepted, quote.TownCanPay));

        request.Confirmed = true;
        request.ExpectedTotal = 40;
        var result = await _s.Trading.SellAsync(_s.Bakery, request);
        Assert.Equal((40L, 90L), (result.Total, result.CoinsAfter!.Value));
        Assert.Equal(90, _s.Coins(_s.Bob));
        Assert.Equal(460, _s.TownCoins());
        Assert.Equal(4, E.Amount(_s.Merchandise(_s.Bakery), Bread));
        Assert.Equal(StockReason.ShopBuyBack, E.Db.StockTransactions.AsNoTracking().Single(t => t.Id == result.StockTransactionIds[0]).Reason);
        Assert.True((await _s.Accounts.GetByTownAsync(ProductionWorldTestDb.Phoenix))!.Reconciled);

        var replay = await _s.Trading.SellAsync(_s.Bakery, request);
        Assert.True(replay.Replayed);
        Assert.Equal(90, _s.Coins(_s.Bob));
    }

    [Fact]
    public async Task AShop_RefusesCategoriesItDoesntBuy_AndAcceptsSubCategories()
    {
        await _s.SetUpAsync();
        await _s.FundTownAsync(500);
        var bakery = await _s.Trading.SellQuoteAsync(_s.Bakery, new ShopSellRequestDto { UserId = _s.Bob, Lines = new() { new ShopSellLineDto { ItemBlueprintId = Sword } } });
        Assert.Equal(("CategoryNotAccepted", false), (bakery.Lines[0].Refusal, bakery.Accepted));
        var smith = await _s.Trading.SellQuoteAsync(_s.Weaponsmith, new ShopSellRequestDto { UserId = _s.Bob, Lines = new() { new ShopSellLineDto { ItemBlueprintId = Sword } } });
        Assert.Equal(((string?)null, 50L), (smith.Lines[0].Refusal, smith.Total));
        var ex = await Assert.ThrowsAsync<ShopRefusedException>(() => _s.Trading.SellAsync(_s.Bakery, new ShopSellRequestDto
        {
            UserId = _s.Bob, Confirmed = true, ExpectedTotal = 10, IdempotencyKey = "mixed",
            Lines = new() { new ShopSellLineDto { ItemBlueprintId = Bread }, new ShopSellLineDto { ItemBlueprintId = Sword } },
        }));
        Assert.Equal("CategoryNotAccepted", ex.Code);
        Assert.Equal(50, _s.Coins(_s.Bob));
        Assert.Equal(0, E.Amount(_s.Merchandise(_s.Bakery), Bread));
    }

    [Fact]
    public async Task ASoldEnchantedItem_BecomesAStockUnit_AndIsResoldAsThatExactItem()
    {
        await _s.SetUpAsync();
        await _s.FundTownAsync(500);
        long sword = _s.Instance(Sword, (2, "sharpness"), (1, "unbreaking"));
        // Sell price 100 × (1 + 3 × 10%) = 130; buy-back 65.
        var sold = await _s.Trading.SellAsync(_s.Weaponsmith, new ShopSellRequestDto
        {
            UserId = _s.Bob, Confirmed = true, ExpectedTotal = 65, IdempotencyKey = "sword",
            Lines = new() { new ShopSellLineDto { ItemInstanceId = sword } },
        });
        Assert.Equal(65, sold.Total);
        var unit = E.Db.StockUnits.AsNoTracking().Single(u => u.ItemInstanceId == sword);
        Assert.Equal(_s.Merchandise(_s.Weaponsmith), unit.StorageId);
        Assert.Equal(2, E.Db.Set<ItemInstanceEnchantment>().Count(e => e.ItemInstanceId == sword));

        var item = (await _s.Trading.OfferAsync(_s.Weaponsmith)).Items.Single(i => i.ItemInstanceId == sword);
        Assert.Equal((130L, 1L), (item.UnitPrice!.Value, item.Stock!.Value));
        Assert.Equal(new[] { "sharpness 2", "unbreaking 1" }, item.Enchantments!.OrderBy(x => x).ToArray());

        var bought = await _s.Trading.PurchaseAsync(_s.Weaponsmith, new ShopPurchaseRequestDto
        {
            UserId = _s.Ann, ItemInstanceId = sword, Amount = 1, ExpectedUnitPrice = 130, Confirmed = true, IdempotencyKey = "resell",
        });
        Assert.Equal(sword, bought.ItemInstanceId);
        Assert.False(E.Db.StockUnits.Any(u => u.ItemInstanceId == sword));
        Assert.Equal(_s.Ann, E.Db.ItemInstances.AsNoTracking().Single(i => i.Id == sword).OwnerUserId);
        // No longer on offer at all: it left the shop's Merchandise.
        Assert.Equal("NotForSale", (await _s.Trading.QuoteAsync(_s.Weaponsmith, new ShopPurchaseRequestDto { UserId = _s.Ann, ItemInstanceId = sword, Amount = 1 })).Refusal);
    }

    [Fact]
    public async Task SellingNeedsProductionEnabled_AndATownThatCanPay_AndRoom()
    {
        using (var off = new ShopTestDb(productionEnabled: false))
        {
            await off.SetUpAsync();
            await Assert.ThrowsAsync<ProductionOffException>(() =>
                off.Trading.SellQuoteAsync(off.Bakery, new ShopSellRequestDto { UserId = off.Bob, Lines = new() { new ShopSellLineDto { ItemBlueprintId = Bread } } }));
        }
        await _s.SetUpAsync();
        var poor = await Assert.ThrowsAsync<ShopRefusedException>(() => _s.Trading.SellAsync(_s.Bakery, new ShopSellRequestDto
        {
            UserId = _s.Bob, Confirmed = true, ExpectedTotal = 10, IdempotencyKey = "poor", Lines = new() { new ShopSellLineDto { ItemBlueprintId = Bread } },
        }));
        Assert.Equal("TownFundsInsufficient", poor.Code);

        await _s.FundTownAsync(5000);
        var full = await Assert.ThrowsAsync<ShopRefusedException>(() => _s.Trading.SellAsync(_s.Bakery, new ShopSellRequestDto
        {
            UserId = _s.Bob, Confirmed = true, ExpectedTotal = 2010, IdempotencyKey = "full", Lines = new() { new ShopSellLineDto { ItemBlueprintId = Bread, Amount = 201 } },
        }));
        Assert.Equal("NoRoom", full.Code); // Merchandise is capped at 200 at level 1
    }

    // ================= Town account =================

    [Fact]
    public async Task TheTownReasons_PostOnlyThroughATownAccount_AndCantBeReversed()
    {
        await _s.SetUpAsync();
        await Assert.ThrowsAsync<CurrencyException>(() => _s.Currency.SpendAsync(_s.Ann, Currency.Coins, 5,
            CurrencyContext.ForSystem("test", CurrencyReasons.ShopPurchase, "direct")));
        await _s.Entry(_s.Bakery, Bread, price: 5, mode: "Unlimited");
        var r = await _s.Trading.PurchaseAsync(_s.Bakery, Buy(_s.Ann, Bread, 1, 5, "rev"));
        var txId = E.Db.CurrencyTransactions.AsNoTracking().Single(t => t.PublicId == r.CurrencyTransactionId).Id;
        var ex = await Assert.ThrowsAsync<CurrencyException>(() => _s.Currency.ReverseAsync(txId, new ReversalOptions(), new CurrencyContext
        {
            IdempotencyKey = "rev-x", ReasonCode = CurrencyReasons.Reversal, Reason = "test", Initiator = CurrencyInitiator.Admin, InitiatorUserId = 1,
        }));
        Assert.Equal(CurrencyErrorCode.NotReversible, ex.Code);
        await Assert.ThrowsAsync<CurrencyException>(() => _s.Currency.PostWithTownAsync(ProductionWorldTestDb.Phoenix,
            new[] { new CurrencyLeg(_s.Ann, Currency.Coins, 5) }, CurrencyContext.ForSystem("test", CurrencyReasons.Salary, "salary-town")));
    }
}

internal static class ShopRequestExtensions
{
    public static ShopPurchaseRequestDto WithInbox(this ShopPurchaseRequestDto r)
    {
        r.DeliverTo = "Inbox";
        return r;
    }
}
