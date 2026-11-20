using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services.SiegeShop;

/// <summary>
/// The default catalogue of siege-shop DESIGN § 12.2 (PandiO/knk-workspace#867): PvP Siege consumables with their brakes
/// plus one weapon entry per existing template at the template's price, Siege Survival the same consumables at ×10, the
/// potions described by their blueprints' potion type, seeded once per mode and never over an admin's list.
/// </summary>
public class SiegeShopSeedTests : IAsyncLifetime
{
    private KnKDbContext _context = null!;

    public async Task InitializeAsync()
    {
        _context = SiegeTestData.NewContext("SiegeShopSeed");
        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    private async Task<List<SiegeShopEntry>> Entries(SiegeGameMode mode) => await _context.SiegeShopEntries
        .Include(e => e.ItemBlueprint).Include(e => e.Category).Include(e => e.SiegeWeaponTemplate)
        .Where(e => e.GameMode == mode).OrderBy(e => e.Kind).ThenBy(e => e.SortOrder).ToListAsync();

    [Fact]
    public async Task FreshDatabase_GetsTheSection12Catalogue()
    {
        _context.SiegeWeaponTemplates.Add(new SiegeWeaponTemplate { Name = "Trebuchet", MatchPrice = 800 });
        _context.SiegeWeaponTemplates.Add(new SiegeWeaponTemplate { Name = "Ballista", Kind = SiegeWeaponKind.Direct });
        await _context.SaveChangesAsync();

        await SiegeShopSeed.SeedCanonicalAsync(_context);
        _context.ChangeTracker.Clear();

        var siege = await Entries(SiegeGameMode.Siege);
        var items = siege.Where(e => e.Kind == SiegeShopEntryKind.Item).ToList();
        Assert.Equal(new[] { "Ender Pearl", "Splash Potion of Healing II", "Golden Apple", "Potion of Regeneration" },
            items.Select(e => e.ItemBlueprint!.Name));
        Assert.All(items, e => Assert.Equal("consumables", e.Category.Key));

        var pearl = items[0];
        Assert.Equal((300, 50, SiegeShopCurve.Linear), (pearl.Price!.Value, pearl.PriceStep!.Value, pearl.PriceCurve));
        Assert.Equal((20, 10, SiegeShopCurve.Exponential, 1.5), (pearl.CooldownSeconds!.Value, pearl.CooldownStepSeconds!.Value, pearl.CooldownCurve, pearl.CooldownGrowthFactor!.Value));
        Assert.Equal(8, pearl.MaxPerMatch);
        var healing = items[1];
        Assert.Equal((150, 25, 10, 10), (healing.Price!.Value, healing.PriceStep!.Value, healing.CooldownSeconds!.Value, healing.MaxPerMatch!.Value));
        Assert.Equal((200, 50, 6), (items[2].Price!.Value, items[2].PriceStep!.Value, items[2].MaxPerMatch!.Value));
        Assert.Null(items[2].CooldownSeconds);
        Assert.Equal((250, 4), (items[3].Price!.Value, items[3].MaxPerMatch!.Value));
        Assert.Null(items[3].PriceStep);

        // Healing II splash and regeneration are real potions (DESIGN § 9.4).
        Assert.Equal("minecraft:strong_healing", healing.ItemBlueprint!.PotionType);
        Assert.Equal("minecraft:regeneration", items[3].ItemBlueprint!.PotionType);

        var weapons = siege.Where(e => e.Kind == SiegeShopEntryKind.SiegeWeapon).ToList();
        Assert.Equal(new[] { "Trebuchet", "Ballista" }, weapons.Select(e => e.SiegeWeaponTemplate!.Name));
        Assert.All(weapons, e => { Assert.Null(e.Price); Assert.Equal("siege-weapons", e.Category.Key); });

        var survival = await Entries(SiegeGameMode.SiegeSurvival);
        Assert.Equal(new int?[] { 3000, 1500, 2000, 2500 }, survival.Select(e => e.Price));
        Assert.Equal(new int?[] { 500, 250, 500, null }, survival.Select(e => e.PriceStep));
        Assert.DoesNotContain(survival, e => e.Kind == SiegeShopEntryKind.SiegeWeapon);
        Assert.All(survival, e => Assert.Null(e.SiegeScenarioId));
    }

    [Fact]
    public async Task SecondRun_AddsNothing_AndAnAdminsListIsKept()
    {
        await SiegeShopSeed.SeedCanonicalAsync(_context);
        var first = await _context.SiegeShopEntries.CountAsync();
        var blueprints = await _context.ItemBlueprints.CountAsync();

        // An admin deletes every Survival entry but one: the mode still has a list, so nothing comes back.
        _context.SiegeShopEntries.RemoveRange(_context.SiegeShopEntries.Where(e => e.GameMode == SiegeGameMode.SiegeSurvival).Skip(1));
        await _context.SaveChangesAsync();
        await SiegeShopSeed.SeedCanonicalAsync(_context);

        Assert.Equal(first - 3, await _context.SiegeShopEntries.CountAsync());
        Assert.Equal(blueprints, await _context.ItemBlueprints.CountAsync());
    }

    [Fact]
    public async Task ExistingBlueprints_AreReusedByName()
    {
        _context.ItemBlueprints.Add(new ItemBlueprint { Name = "golden apple", DefaultDisplayName = "Shiny" });
        await _context.SaveChangesAsync();

        await SiegeShopSeed.SeedCanonicalAsync(_context);

        Assert.Equal(1, await _context.ItemBlueprints.CountAsync(b => b.Name!.ToLower() == "golden apple"));
        Assert.Equal(3, await _context.ItemBlueprints.CountAsync() - 1);
    }
}
