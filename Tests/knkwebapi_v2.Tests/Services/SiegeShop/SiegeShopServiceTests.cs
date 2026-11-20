using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Services.SiegeShop;

/// <summary>
/// Siege shop catalogue (knk-workspace docs/specs/siege-shop/DESIGN.md § 5.3, § 6.1; PandiO/knk-workspace#867): the
/// categories, entries and overrides save and read back through the real service and repository (InMemory, A13), an
/// entry whose target doesn't match its kind is refused (A14), the effective catalogue hides, overrides and adds
/// (A15) and a weapon entry without a price takes the template's MatchPrice (A16).
/// </summary>
public class SiegeShopServiceTests : IAsyncLifetime
{
    private const int ScenarioId = 100;
    private const int OtherScenarioId = 101;
    private const int ConsumablesId = 2; // seeded by the model (HasData)
    private const int WeaponsId = 1;
    private const int MagicId = 3;

    private KnKDbContext _context = null!;
    private int _pearlId;
    private int _appleId;
    private int _trebuchetId;

    public async Task InitializeAsync()
    {
        _context = SiegeTestData.NewContext("SiegeShop");
        await _context.Database.EnsureCreatedAsync(); // the three seeded categories
        await SiegeTestData.SeedValidScenarioAsync(_context);
        _context.SiegeScenarios.Add(new SiegeScenario { Id = OtherScenarioId, Name = "Other", TownId = 1, HubLocationId = 1000 });
        var pearl = new ItemBlueprint { Name = "Ender Pearl", DefaultDisplayName = "Ender Pearl" };
        var apple = new ItemBlueprint { Name = "Golden Apple", DefaultDisplayName = "Golden Apple" };
        var trebuchet = new SiegeWeaponTemplate { Name = "Trebuchet", MatchPrice = 800 };
        _context.ItemBlueprints.AddRange(pearl, apple);
        _context.SiegeWeaponTemplates.Add(trebuchet);
        await _context.SaveChangesAsync();
        (_pearlId, _appleId, _trebuchetId) = (pearl.Id, apple.Id, trebuchet.Id);
        _context.ChangeTracker.Clear();
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    private SiegeShopService Service() => new(new SiegeShopRepository(_context), SiegeTestData.Mapper());

    private SiegeShopEntryUpsertDto Pearl() => new()
    {
        GameMode = SiegeGameMode.Siege, CategoryId = ConsumablesId, Kind = SiegeShopEntryKind.Item, ItemBlueprintId = _pearlId,
        Quantity = 1, Price = 300, PriceStep = 50, PriceCurve = SiegeShopCurve.Linear, CooldownSeconds = 20,
        CooldownStepSeconds = 10, CooldownCurve = SiegeShopCurve.Exponential, CooldownGrowthFactor = 1.5, MaxPerMatch = 8,
        SortOrder = 0, Enabled = true,
    };

    private SiegeShopEntryUpsertDto Apple() => new()
    {
        GameMode = SiegeGameMode.Siege, CategoryId = ConsumablesId, Kind = SiegeShopEntryKind.Item, ItemBlueprintId = _appleId,
        Quantity = 1, Price = 200, PriceStep = 50, PriceCurve = SiegeShopCurve.Linear, MaxPerMatch = 6, SortOrder = 2,
    };

    private SiegeShopEntryUpsertDto Trebuchet(int? price = null) => new()
    {
        GameMode = SiegeGameMode.Siege, CategoryId = WeaponsId, Kind = SiegeShopEntryKind.SiegeWeapon,
        SiegeWeaponTemplateId = _trebuchetId, Quantity = 1, Price = price,
    };

    // ---- A13: categories, entries and overrides save and read back ----

    [Fact]
    public async Task Categories_AreSeeded_WithStableKeys()
    {
        var categories = await Service().GetCategoriesAsync();
        Assert.Equal(new[] { "siege-weapons", "consumables", "magic" }, categories.Select(c => c.Key));
    }

    [Fact]
    public async Task Entry_SavesEveryBrakeField_AndReadsBackWithNames()
    {
        var created = await Service().CreateEntryAsync(Pearl());
        _context.ChangeTracker.Clear();

        var read = (await Service().GetEntryByIdAsync(created.Id))!;
        Assert.Equal(SiegeGameMode.Siege, read.GameMode);
        Assert.Null(read.SiegeScenarioId);
        Assert.Equal("Consumables", read.CategoryName);
        Assert.Equal("Ender Pearl", read.ItemBlueprintName);
        Assert.Equal(300, read.Price);
        Assert.Equal(50, read.PriceStep);
        Assert.Equal(SiegeShopCurve.Linear, read.PriceCurve);
        Assert.Equal(20, read.CooldownSeconds);
        Assert.Equal(10, read.CooldownStepSeconds);
        Assert.Equal(SiegeShopCurve.Exponential, read.CooldownCurve);
        Assert.Equal(1.5, read.CooldownGrowthFactor);
        Assert.Equal(8, read.MaxPerMatch);
        Assert.True(read.Enabled);
    }

    [Fact]
    public async Task Entry_Update_ChangesTheFields()
    {
        var created = await Service().CreateEntryAsync(Pearl());
        _context.ChangeTracker.Clear();
        var dto = Pearl();
        dto.Price = 350;
        dto.Enabled = false;
        dto.DisplayName = "  Pearl  ";
        await Service().UpdateEntryAsync(created.Id, dto);
        _context.ChangeTracker.Clear();

        var read = (await Service().GetEntryByIdAsync(created.Id))!;
        Assert.Equal(350, read.Price);
        Assert.False(read.Enabled);
        Assert.Equal("Pearl", read.DisplayName);
    }

    [Fact]
    public async Task Category_CreateUpdate_AndKeyRules()
    {
        var created = await Service().CreateCategoryAsync(new SiegeShopCategoryUpsertDto { Key = "Traps", Name = "Traps", SortOrder = 5 });
        Assert.Equal("traps", created.Key);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().CreateCategoryAsync(new SiegeShopCategoryUpsertDto { Key = "consumables", Name = "Again" }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service().CreateCategoryAsync(new SiegeShopCategoryUpsertDto { Key = "bad key!", Name = "Bad" }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service().CreateCategoryAsync(new SiegeShopCategoryUpsertDto { Key = "ok", Name = " " }));
    }

    [Fact]
    public async Task Category_WithEntries_CannotBeDeleted_ButAnEmptyOneCan()
    {
        await Service().CreateEntryAsync(Pearl());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Service().DeleteCategoryAsync(ConsumablesId));
        Assert.Contains("1 shop entry", ex.Message);

        await Service().DeleteCategoryAsync(MagicId);
        Assert.Null(await Service().GetCategoryByIdAsync(MagicId));
    }

    [Fact]
    public async Task Override_SavesAndReadsBack_ThroughTheScenario()
    {
        var entry = await Service().CreateEntryAsync(Pearl());
        var created = await Service().CreateOverrideAsync(ScenarioId, new SiegeScenarioShopOverrideUpsertDto
        {
            SiegeShopEntryId = entry.Id, Price = 500, MaxPerMatch = 2,
        });
        _context.ChangeTracker.Clear();

        var list = await Service().GetOverridesAsync(ScenarioId);
        var read = Assert.Single(list);
        Assert.Equal(created.Id, read.Id);
        Assert.Equal("Ender Pearl", read.SiegeShopEntryName);
        Assert.Equal(500, read.Price);
        Assert.Equal(2, read.MaxPerMatch);
        Assert.Null(read.Quantity);
        Assert.False(read.Hidden);

        await Service().UpdateOverrideAsync(created.Id, new SiegeScenarioShopOverrideUpsertDto { SiegeShopEntryId = entry.Id, Hidden = true });
        _context.ChangeTracker.Clear();
        var updated = (await Service().GetOverrideByIdAsync(created.Id))!;
        Assert.True(updated.Hidden);
        Assert.Null(updated.Price);
    }

    [Fact]
    public async Task Override_OnePerScenarioAndEntry_OnlyForDefaultEntries()
    {
        var entry = await Service().CreateEntryAsync(Pearl());
        await Service().CreateOverrideAsync(ScenarioId, new SiegeScenarioShopOverrideUpsertDto { SiegeShopEntryId = entry.Id });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().CreateOverrideAsync(ScenarioId, new SiegeScenarioShopOverrideUpsertDto { SiegeShopEntryId = entry.Id }));

        var own = Apple();
        own.SiegeScenarioId = ScenarioId;
        var scenarioOnly = await Service().CreateEntryAsync(own);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Service().CreateOverrideAsync(ScenarioId, new SiegeScenarioShopOverrideUpsertDto { SiegeShopEntryId = scenarioOnly.Id }));
        Assert.Contains("default entries", ex.Message);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Service().CreateOverrideAsync(999, new SiegeScenarioShopOverrideUpsertDto { SiegeShopEntryId = entry.Id }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service().CreateOverrideAsync(ScenarioId, new SiegeScenarioShopOverrideUpsertDto { SiegeScenarioId = OtherScenarioId, SiegeShopEntryId = entry.Id }));
    }

    [Fact]
    public async Task DeletingAnEntry_RemovesItsOverrides()
    {
        var entry = await Service().CreateEntryAsync(Pearl());
        await Service().CreateOverrideAsync(ScenarioId, new SiegeScenarioShopOverrideUpsertDto { SiegeShopEntryId = entry.Id, Hidden = true });
        _context.ChangeTracker.Clear();

        await Service().DeleteEntryAsync(entry.Id);
        _context.ChangeTracker.Clear();
        Assert.Empty(await Service().GetOverridesAsync(ScenarioId));
    }

    // ---- A14: the target must match the kind ----

    public static IEnumerable<object[]> MismatchedTargets() => new List<object[]>
    {
        new object[] { SiegeShopEntryKind.Item, true, true, "not a siege weapon template" },
        new object[] { SiegeShopEntryKind.Item, false, false, "needs an item blueprint" },
        new object[] { SiegeShopEntryKind.SiegeWeapon, true, true, "not an item blueprint" },
        new object[] { SiegeShopEntryKind.SiegeWeapon, false, false, "needs a siege weapon template" },
    };

    [Theory]
    [MemberData(nameof(MismatchedTargets))]
    public async Task Entry_WhoseTargetDoesNotMatchItsKind_IsRefused(SiegeShopEntryKind kind, bool blueprint, bool template, string message)
    {
        var dto = new SiegeShopEntryUpsertDto
        {
            CategoryId = ConsumablesId, Kind = kind, Price = 100, Quantity = 1,
            ItemBlueprintId = blueprint ? _pearlId : null, SiegeWeaponTemplateId = template ? _trebuchetId : null,
        };
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Service().CreateEntryAsync(dto));
        Assert.Contains(message, ex.Message);
    }

    public static IEnumerable<object[]> BadEntries() => new List<object[]>
    {
        new object[] { (Action<SiegeShopEntryUpsertDto>)(d => d.Price = null), "needs a price" },
        new object[] { (Action<SiegeShopEntryUpsertDto>)(d => d.Price = -1), "price" },
        new object[] { (Action<SiegeShopEntryUpsertDto>)(d => d.Quantity = 0), "quantity" },
        new object[] { (Action<SiegeShopEntryUpsertDto>)(d => d.PriceStep = -5), "priceStep" },
        new object[] { (Action<SiegeShopEntryUpsertDto>)(d => d.PriceMax = 100), "priceMax" },
        new object[] { (Action<SiegeShopEntryUpsertDto>)(d => d.CooldownGrowthFactor = 1), "cooldownGrowthFactor" },
        new object[] { (Action<SiegeShopEntryUpsertDto>)(d => { d.PriceCurve = SiegeShopCurve.Exponential; d.PriceGrowthFactor = 0.5; }), "priceGrowthFactor" },
        new object[] { (Action<SiegeShopEntryUpsertDto>)(d => d.CooldownSeconds = null), "base cooldown" },
        new object[] { (Action<SiegeShopEntryUpsertDto>)(d => d.CooldownMaxSeconds = 5), "cooldownMaxSeconds" },
        new object[] { (Action<SiegeShopEntryUpsertDto>)(d => d.MaxPerMatch = 0), "maxPerMatch" },
        new object[] { (Action<SiegeShopEntryUpsertDto>)(d => d.CategoryId = 0), "category" },
        new object[] { (Action<SiegeShopEntryUpsertDto>)(d => d.CategoryId = 99), "SiegeShopCategory" },
        new object[] { (Action<SiegeShopEntryUpsertDto>)(d => d.SiegeScenarioId = 999), "SiegeScenario" },
        new object[] { (Action<SiegeShopEntryUpsertDto>)(d => d.ItemBlueprintId = 9999), "ItemBlueprint" },
    };

    [Theory]
    [MemberData(nameof(BadEntries))]
    public async Task Entry_WithABadValue_IsRefused(Action<SiegeShopEntryUpsertDto> change, string message)
    {
        var dto = Pearl();
        change(dto);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Service().CreateEntryAsync(dto));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public async Task WeaponEntry_SellsOneWeaponPerPurchase()
    {
        var dto = Trebuchet();
        dto.Quantity = 2;
        await Assert.ThrowsAsync<ArgumentException>(() => Service().CreateEntryAsync(dto));
    }

    [Fact]
    public async Task ZeroForAnOptionalReference_MeansNone()
    {
        var dto = Pearl();
        dto.SiegeScenarioId = 0;
        dto.SiegeWeaponTemplateId = 0;
        var created = await Service().CreateEntryAsync(dto);
        Assert.Null(created.SiegeScenarioId);
        Assert.Null(created.SiegeWeaponTemplateId);
    }

    // ---- A15, A16: the effective catalogue ----

    [Fact]
    public async Task Catalogue_WithoutScenario_ListsTheModesEnabledDefaults_ByCategoryAndOrder()
    {
        await Service().CreateEntryAsync(Apple());
        await Service().CreateEntryAsync(Pearl());
        await Service().CreateEntryAsync(Trebuchet());
        var disabled = Apple();
        disabled.Enabled = false;
        await Service().CreateEntryAsync(disabled);
        var survival = Pearl();
        survival.GameMode = SiegeGameMode.SiegeSurvival;
        survival.Price = 3000;
        await Service().CreateEntryAsync(survival);
        var own = Apple();
        own.SiegeScenarioId = ScenarioId;
        await Service().CreateEntryAsync(own);

        var catalogue = await Service().GetCatalogueAsync(SiegeGameMode.Siege, null);

        // Magic has no entries and is left out; weapons (order 0) before consumables (order 1).
        Assert.Equal(new[] { "siege-weapons", "consumables" }, catalogue.Categories.Select(c => c.Key));
        var consumables = catalogue.Categories[1].Entries;
        Assert.Equal(new[] { "Ender Pearl", "Golden Apple" }, consumables.Select(e => e.ItemBlueprintName));
        Assert.All(consumables, e => Assert.Equal(SiegeShopCatalogueResolver.SourceDefault, e.Source));

        var survivalCatalogue = await Service().GetCatalogueAsync(SiegeGameMode.SiegeSurvival, null);
        Assert.Equal(3000, Assert.Single(Assert.Single(survivalCatalogue.Categories).Entries).Price);
    }

    [Fact]
    public async Task Catalogue_ForAScenario_HidesAppliesOverridesAndAddsItsOwnEntries_A15()
    {
        var pearl = await Service().CreateEntryAsync(Pearl());
        var apple = await Service().CreateEntryAsync(Apple());
        var own = Apple();
        own.SiegeScenarioId = ScenarioId;
        own.DisplayName = "Scenario apple";
        own.Price = 50;
        own.SortOrder = 9;
        var scenarioOnly = await Service().CreateEntryAsync(own);
        var elsewhere = Apple();
        elsewhere.SiegeScenarioId = OtherScenarioId;
        await Service().CreateEntryAsync(elsewhere);

        await Service().CreateOverrideAsync(ScenarioId, new SiegeScenarioShopOverrideUpsertDto { SiegeShopEntryId = apple.Id, Hidden = true });
        await Service().CreateOverrideAsync(ScenarioId, new SiegeScenarioShopOverrideUpsertDto
        {
            SiegeShopEntryId = pearl.Id, Price = 600, Quantity = 2, MaxPerMatch = 3, CooldownSeconds = 40, PriceStep = 100,
            CooldownStepSeconds = 5, SortOrder = 4,
        });
        // Another scenario's override never leaks in.
        await Service().CreateOverrideAsync(OtherScenarioId, new SiegeScenarioShopOverrideUpsertDto { SiegeShopEntryId = pearl.Id, Hidden = true });
        _context.ChangeTracker.Clear();

        var catalogue = await Service().GetCatalogueAsync(SiegeGameMode.Siege, ScenarioId);

        var entries = Assert.Single(catalogue.Categories).Entries;
        Assert.Equal(new[] { pearl.Id, scenarioOnly.Id }, entries.Select(e => e.Id));

        var overridden = entries[0];
        Assert.Equal(SiegeShopCatalogueResolver.SourceOverridden, overridden.Source);
        Assert.Equal(600, overridden.Price);
        Assert.Equal(2, overridden.Quantity);
        Assert.Equal(3, overridden.MaxPerMatch);
        Assert.Equal(40, overridden.CooldownSeconds);
        Assert.Equal(100, overridden.PriceStep);
        Assert.Equal(5, overridden.CooldownStepSeconds);
        Assert.Equal(4, overridden.SortOrder);
        // Fields without an override column keep the default.
        Assert.Equal(SiegeShopCurve.Linear, overridden.PriceCurve);
        Assert.Equal(SiegeShopCurve.Exponential, overridden.CooldownCurve);

        var added = entries[1];
        Assert.Equal(SiegeShopCatalogueResolver.SourceScenario, added.Source);
        Assert.Equal("Scenario apple", added.DisplayName);
        Assert.Equal(50, added.Price);
    }

    [Fact]
    public async Task Catalogue_AnEmptyOverride_KeepsTheDefault()
    {
        var pearl = await Service().CreateEntryAsync(Pearl());
        await Service().CreateOverrideAsync(ScenarioId, new SiegeScenarioShopOverrideUpsertDto { SiegeShopEntryId = pearl.Id });

        var entry = Assert.Single(Assert.Single((await Service().GetCatalogueAsync(SiegeGameMode.Siege, ScenarioId)).Categories).Entries);
        Assert.Equal(SiegeShopCatalogueResolver.SourceDefault, entry.Source);
        Assert.Equal(300, entry.Price);
        Assert.Equal(8, entry.MaxPerMatch);
    }

    [Fact]
    public async Task Catalogue_WeaponEntryWithoutPrice_UsesTheTemplatesMatchPrice_A16()
    {
        var plain = await Service().CreateEntryAsync(Trebuchet());
        Assert.Null(plain.Price);

        var entry = Assert.Single(Assert.Single((await Service().GetCatalogueAsync(SiegeGameMode.Siege, null)).Categories).Entries);
        Assert.Equal(800, entry.Price);
        Assert.Equal("Trebuchet", entry.SiegeWeaponTemplateName);
        Assert.Equal(SiegeShopEntryKind.SiegeWeapon, entry.Kind);

        // Its own price wins over the template's, and a scenario's override wins over both.
        var dto = Trebuchet(price: 650);
        await Service().UpdateEntryAsync(plain.Id, dto);
        _context.ChangeTracker.Clear();
        Assert.Equal(650, (await Service().GetCatalogueAsync(SiegeGameMode.Siege, null)).Categories[0].Entries[0].Price);

        await Service().CreateOverrideAsync(ScenarioId, new SiegeScenarioShopOverrideUpsertDto { SiegeShopEntryId = plain.Id, Price = 400 });
        Assert.Equal(400, (await Service().GetCatalogueAsync(SiegeGameMode.Siege, ScenarioId)).Categories[0].Entries[0].Price);
    }

    [Fact]
    public async Task Catalogue_UnknownScenario_IsNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service().GetCatalogueAsync(SiegeGameMode.Siege, 999));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service().GetCatalogueAsync(SiegeGameMode.Siege, 0));
    }

    [Fact]
    public async Task Scenario_ReadDto_ListsItsOverridesAndOwnEntries()
    {
        var pearl = await Service().CreateEntryAsync(Pearl());
        await Service().CreateOverrideAsync(ScenarioId, new SiegeScenarioShopOverrideUpsertDto { SiegeShopEntryId = pearl.Id, Hidden = true });
        var own = Apple();
        own.SiegeScenarioId = ScenarioId;
        await Service().CreateEntryAsync(own);
        _context.ChangeTracker.Clear();

        var scenario = await new SiegeScenarioRepository(_context).GetByIdAsync(ScenarioId);
        var dto = SiegeTestData.Mapper().Map<SiegeScenarioReadDto>(scenario);
        Assert.Equal("Ender Pearl", Assert.Single(dto.ShopOverrides).SiegeShopEntryName);
        Assert.Equal("Golden Apple", Assert.Single(dto.ShopEntries).ItemBlueprintName);
    }

    [Fact]
    public async Task Search_FiltersDefaultsOnly_ForTheOverridePicker()
    {
        await Service().CreateEntryAsync(Pearl());
        var own = Apple();
        own.SiegeScenarioId = ScenarioId;
        await Service().CreateEntryAsync(own);

        var defaults = await Service().SearchEntriesAsync(new PagedQueryDto
        {
            PageNumber = 1, PageSize = 10, Filters = new Dictionary<string, string> { ["defaultsOnly"] = "true" },
        });
        Assert.Equal("Ender Pearl", Assert.Single(defaults.Items).Name);

        var scenarioOnly = await Service().SearchEntriesAsync(new PagedQueryDto
        {
            PageNumber = 1, PageSize = 10, Filters = new Dictionary<string, string> { ["defaultsOnly"] = "false" },
        });
        Assert.Equal("Siege of Cinix", Assert.Single(scenarioOnly.Items).SiegeScenarioName);
    }
}
