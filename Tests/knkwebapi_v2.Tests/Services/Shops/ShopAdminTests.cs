using System.Reflection;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using KnKWebAPI.Controllers;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static knkwebapi_v2.Tests.Services.Shops.ShopTestDb;

namespace knkwebapi_v2.Tests.Services.Shops;

/// <summary>Shops P10 (#248): shop types, levels, settings and the access rules of the game server's routes.</summary>
public class ShopAdminTests : IDisposable
{
    private readonly ShopTestDb _s = new();

    public void Dispose() => _s.Dispose();

    [Fact]
    public async Task AShopType_HasAUniqueName_ItsCategories_AndCantBeDeletedWhileUsed()
    {
        await _s.SetUpAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _s.Types.CreateAsync(new ShopTypeDto { Name = "Bakery" }));
        await Assert.ThrowsAsync<ArgumentException>(() => _s.Types.CreateAsync(new ShopTypeDto { Name = "X", AcceptedCategoryIds = new() { 12345 } }));
        await Assert.ThrowsAsync<ArgumentException>(() => _s.Types.CreateAsync(new ShopTypeDto { Name = "Y", DefaultKind = "Bank" }));
        var smith = (await _s.Types.SearchAsync(new PagedQueryDto { SearchTerm = "weapon" })).Items.Single();
        Assert.Equal((1, 0), (smith.AcceptedCategoryCount, smith.LevelCount));
        var updated = await _s.Types.UpdateAsync(smith.Id, new ShopTypeDto { AcceptedCategoryIds = new() { Weapons, Food } });
        Assert.Equal(new[] { Food, Weapons }, updated.AcceptedCategoryIds!.ToArray());
        await Assert.ThrowsAsync<InvalidOperationException>(() => _s.Types.DeleteAsync(smith.Id));
    }

    [Fact]
    public async Task ALevel_IsOncePerType_AndItsCapsReachTheShopsAtThatLevel()
    {
        await _s.SetUpAsync();
        var bakeryType = _s.E.Db.Set<knkwebapi_v2.Models.ShopType>().AsNoTracking().Single(t => t.Name == "Bakery").Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _s.Types.CreateLevelAsync(new ShopTypeLevelDto { ShopTypeId = bakeryType, Level = 1 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _s.Types.CreateLevelAsync(new ShopTypeLevelDto { ShopTypeId = bakeryType, Level = 3, PriceMultiplier = 0 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _s.Types.CreateLevelAsync(new ShopTypeLevelDto { ShopTypeId = bakeryType, Level = 3, ReorderPointUnits = 10, TargetUnits = 5 }));
        var level1 = (await _s.Types.SearchLevelsAsync(new PagedQueryDto { Filters = new() { ["shopTypeId"] = bakeryType.ToString() } })).Items.First(l => l.Level == 1);
        await _s.Types.UpdateLevelAsync(level1.Id, new ShopTypeLevelDto { MerchandiseMaxUnits = 250, InputMaxUnits = 0 });
        Assert.Equal(250, _s.E.Db.Storages.AsNoTracking().Single(s => s.Id == _s.Merchandise(_s.Bakery)).MaxUnits);
        Assert.Null((await _s.Types.GetLevelAsync(level1.Id))!.InputMaxUnits);
    }

    [Fact]
    public async Task AShop_ValidatesItsPricingFields()
    {
        await _s.SetUpAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => _s.UpdateShopAsync(_s.Bakery, d => d.BuyBackPercent = 120));
        await Assert.ThrowsAsync<ArgumentException>(() => _s.UpdateShopAsync(_s.Bakery, d => d.TaxModifier = 0));
        await Assert.ThrowsAsync<ArgumentException>(() => _s.UpdateShopAsync(_s.Bakery, d => d.Kind = "Bank"));
        await _s.UpdateShopAsync(_s.Bakery, d => d.BuyBackPercent = 25);
        Assert.Equal(25, (await _s.Shops.GetByIdAsync(_s.Bakery))!.BuyBackPercent);
    }

    [Fact]
    public void TheShopSettings_AreValidated()
    {
        var ok = knkwebapi_v2.Services.Territory.ProductionSettings.Normalize(new ProductionSettingsDto
        {
            Shops = new ProductionShopSettingsDto { EnchantmentPercentPerLevel = 5, StatsLookbackHours = 3 },
        });
        Assert.Equal((5m, 3), (ok.Shops!.EnchantmentPercentPerLevel, ok.Shops.StatsLookbackHours));
        Assert.Throws<ArgumentException>(() => knkwebapi_v2.Services.Territory.ProductionSettings.Normalize(new ProductionSettingsDto
        {
            Shops = new ProductionShopSettingsDto { EnchantmentPercentPerLevel = -1 },
        }));
    }

    [Theory]
    [InlineData(nameof(ShopsController.Quote))]
    [InlineData(nameof(ShopsController.Purchase))]
    [InlineData(nameof(ShopsController.SellQuote))]
    [InlineData(nameof(ShopsController.Sell))]
    public void BuyingAndSelling_AreTheGameServersOnly(string action)
    {
        var method = typeof(ShopsController).GetMethod(action)!;
        Assert.NotNull(method.GetCustomAttribute<RequirePluginServiceAttribute>());
    }

    [Fact]
    public void TheShopReasons_AreClassifiedAndTownBalanced()
    {
        Assert.True(CurrencyReasons.IsTownReason(CurrencyReasons.ShopPurchase));
        Assert.True(CurrencyReasons.IsTownReason(CurrencyReasons.ShopBuyBack));
        Assert.False(CurrencyReasons.IsTownReason(CurrencyReasons.ProductionUpgrade));
        Assert.Equal(CurrencyReasonDirection.Debit, CurrencyReasons.Find(CurrencyReasons.ShopPurchase)!.Direction);
        Assert.Equal(CurrencyReasonDirection.Credit, CurrencyReasons.Find(CurrencyReasons.ShopBuyBack)!.Direction);
    }
}
