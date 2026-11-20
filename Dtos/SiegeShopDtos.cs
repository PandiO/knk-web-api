using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Dtos
{
    // Siege shop catalogue (knk-workspace docs/specs/siege-shop/DESIGN.md § 5.3, § 6.1-6.2; PandiO/knk-workspace#867):
    // categories, entries (mode defaults and scenario-only), a scenario's overrides and the effective catalogue.
    // Validation lives in SiegeShopService (ArgumentException -> 400, KeyNotFoundException -> 404,
    // InvalidOperationException -> 409).

    // ---- SiegeShopCategory ----

    public class SiegeShopCategoryReadDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("iconMaterial")] public string? IconMaterial { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
    }

    public class SiegeShopCategoryUpsertDto
    {
        [Required][JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [Required][JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("iconMaterial")] public string? IconMaterial { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
    }

    // ---- SiegeShopEntry ----

    public class SiegeShopEntryReadDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("gameMode")] public SiegeGameMode GameMode { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int? SiegeScenarioId { get; set; }
        [JsonPropertyName("siegeScenarioName")] public string? SiegeScenarioName { get; set; }
        [JsonPropertyName("categoryId")] public int CategoryId { get; set; }
        [JsonPropertyName("categoryName")] public string? CategoryName { get; set; }
        [JsonPropertyName("kind")] public SiegeShopEntryKind Kind { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int? ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("quantity")] public int Quantity { get; set; }
        [JsonPropertyName("siegeWeaponTemplateId")] public int? SiegeWeaponTemplateId { get; set; }
        [JsonPropertyName("siegeWeaponTemplateName")] public string? SiegeWeaponTemplateName { get; set; }
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("iconMaterial")] public string? IconMaterial { get; set; }
        [JsonPropertyName("price")] public int? Price { get; set; }
        [JsonPropertyName("priceStep")] public int? PriceStep { get; set; }
        [JsonPropertyName("priceCurve")] public SiegeShopCurve PriceCurve { get; set; }
        [JsonPropertyName("priceGrowthFactor")] public double? PriceGrowthFactor { get; set; }
        [JsonPropertyName("priceMax")] public int? PriceMax { get; set; }
        [JsonPropertyName("cooldownSeconds")] public int? CooldownSeconds { get; set; }
        [JsonPropertyName("cooldownStepSeconds")] public int? CooldownStepSeconds { get; set; }
        [JsonPropertyName("cooldownCurve")] public SiegeShopCurve CooldownCurve { get; set; }
        [JsonPropertyName("cooldownGrowthFactor")] public double? CooldownGrowthFactor { get; set; }
        [JsonPropertyName("cooldownMaxSeconds")] public int? CooldownMaxSeconds { get; set; }
        [JsonPropertyName("maxPerMatch")] public int? MaxPerMatch { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    }

    // The target must match the kind (Item: itemBlueprintId, no template; SiegeWeapon: siegeWeaponTemplateId, no
    // blueprint). Price null only for weapons (the template's MatchPrice). 0 for an optional reference = none.
    public class SiegeShopEntryUpsertDto
    {
        [JsonPropertyName("gameMode")] public SiegeGameMode GameMode { get; set; } = SiegeGameMode.Siege;
        [JsonPropertyName("siegeScenarioId")] public int? SiegeScenarioId { get; set; }
        [JsonPropertyName("categoryId")] public int CategoryId { get; set; }
        [JsonPropertyName("kind")] public SiegeShopEntryKind Kind { get; set; } = SiegeShopEntryKind.Item;
        [JsonPropertyName("itemBlueprintId")] public int? ItemBlueprintId { get; set; }
        [JsonPropertyName("quantity")] public int Quantity { get; set; } = 1;
        [JsonPropertyName("siegeWeaponTemplateId")] public int? SiegeWeaponTemplateId { get; set; }
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("iconMaterial")] public string? IconMaterial { get; set; }
        [JsonPropertyName("price")] public int? Price { get; set; }
        [JsonPropertyName("priceStep")] public int? PriceStep { get; set; }
        [JsonPropertyName("priceCurve")] public SiegeShopCurve PriceCurve { get; set; } = SiegeShopCurve.None;
        [JsonPropertyName("priceGrowthFactor")] public double? PriceGrowthFactor { get; set; }
        [JsonPropertyName("priceMax")] public int? PriceMax { get; set; }
        [JsonPropertyName("cooldownSeconds")] public int? CooldownSeconds { get; set; }
        [JsonPropertyName("cooldownStepSeconds")] public int? CooldownStepSeconds { get; set; }
        [JsonPropertyName("cooldownCurve")] public SiegeShopCurve CooldownCurve { get; set; } = SiegeShopCurve.None;
        [JsonPropertyName("cooldownGrowthFactor")] public double? CooldownGrowthFactor { get; set; }
        [JsonPropertyName("cooldownMaxSeconds")] public int? CooldownMaxSeconds { get; set; }
        [JsonPropertyName("maxPerMatch")] public int? MaxPerMatch { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    }

    public class SiegeShopEntryListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        // The dashboard's name column: the display name, else the target's name.
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("gameMode")] public SiegeGameMode GameMode { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int? SiegeScenarioId { get; set; }
        [JsonPropertyName("siegeScenarioName")] public string? SiegeScenarioName { get; set; }
        [JsonPropertyName("categoryName")] public string? CategoryName { get; set; }
        [JsonPropertyName("kind")] public SiegeShopEntryKind Kind { get; set; }
        [JsonPropertyName("quantity")] public int Quantity { get; set; }
        [JsonPropertyName("price")] public int? Price { get; set; }
        [JsonPropertyName("maxPerMatch")] public int? MaxPerMatch { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    }

    // ---- SiegeScenarioShopOverride ----

    public class SiegeScenarioShopOverrideReadDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("siegeShopEntryId")] public int SiegeShopEntryId { get; set; }
        [JsonPropertyName("siegeShopEntryName")] public string? SiegeShopEntryName { get; set; }
        [JsonPropertyName("hidden")] public bool Hidden { get; set; }
        [JsonPropertyName("price")] public int? Price { get; set; }
        [JsonPropertyName("quantity")] public int? Quantity { get; set; }
        [JsonPropertyName("maxPerMatch")] public int? MaxPerMatch { get; set; }
        [JsonPropertyName("cooldownSeconds")] public int? CooldownSeconds { get; set; }
        [JsonPropertyName("priceStep")] public int? PriceStep { get; set; }
        [JsonPropertyName("cooldownStepSeconds")] public int? CooldownStepSeconds { get; set; }
        [JsonPropertyName("sortOrder")] public int? SortOrder { get; set; }
    }

    // Every override field is nullable: empty keeps the default entry's value (§ 5.3.1).
    public class SiegeScenarioShopOverrideUpsertDto
    {
        [JsonPropertyName("siegeScenarioId")] public int? SiegeScenarioId { get; set; }
        [JsonPropertyName("siegeShopEntryId")] public int SiegeShopEntryId { get; set; }
        [JsonPropertyName("hidden")] public bool Hidden { get; set; }
        [JsonPropertyName("price")] public int? Price { get; set; }
        [JsonPropertyName("quantity")] public int? Quantity { get; set; }
        [JsonPropertyName("maxPerMatch")] public int? MaxPerMatch { get; set; }
        [JsonPropertyName("cooldownSeconds")] public int? CooldownSeconds { get; set; }
        [JsonPropertyName("priceStep")] public int? PriceStep { get; set; }
        [JsonPropertyName("cooldownStepSeconds")] public int? CooldownStepSeconds { get; set; }
        [JsonPropertyName("sortOrder")] public int? SortOrder { get; set; }
    }

    // ---- Effective catalogue (GET api/SiegeShop/catalogue, § 5.3, § 6.2.2) ----

    public class SiegeShopCatalogueDto
    {
        [JsonPropertyName("gameMode")] public SiegeGameMode GameMode { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int? SiegeScenarioId { get; set; }
        // Categories with at least one visible entry, by (SortOrder, Id).
        [JsonPropertyName("categories")] public List<SiegeShopCatalogueCategoryDto> Categories { get; set; } = new();
    }

    public class SiegeShopCatalogueCategoryDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("iconMaterial")] public string? IconMaterial { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        // Visible entries with overrides applied, by (effective SortOrder, Id).
        [JsonPropertyName("entries")] public List<SiegeShopCatalogueEntryDto> Entries { get; set; } = new();
    }

    // One entry as a match sells it: overrides applied, the weapon price resolved. Weapon entries are always listed;
    // the plugin leaves them out while siege.weapons.enabled is off (§ 5.3.4).
    public class SiegeShopCatalogueEntryDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("categoryId")] public int CategoryId { get; set; }
        [JsonPropertyName("kind")] public SiegeShopEntryKind Kind { get; set; }
        // Default (the mode's list, unchanged), Overridden (a scenario override changed a field) or Scenario (scenario-only).
        [JsonPropertyName("source")] public string Source { get; set; } = "Default";
        [JsonPropertyName("itemBlueprintId")] public int? ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("quantity")] public int Quantity { get; set; }
        [JsonPropertyName("siegeWeaponTemplateId")] public int? SiegeWeaponTemplateId { get; set; }
        [JsonPropertyName("siegeWeaponTemplateName")] public string? SiegeWeaponTemplateName { get; set; }
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("iconMaterial")] public string? IconMaterial { get; set; }
        [JsonPropertyName("price")] public int Price { get; set; }
        [JsonPropertyName("priceStep")] public int? PriceStep { get; set; }
        [JsonPropertyName("priceCurve")] public SiegeShopCurve PriceCurve { get; set; }
        [JsonPropertyName("priceGrowthFactor")] public double? PriceGrowthFactor { get; set; }
        [JsonPropertyName("priceMax")] public int? PriceMax { get; set; }
        [JsonPropertyName("cooldownSeconds")] public int? CooldownSeconds { get; set; }
        [JsonPropertyName("cooldownStepSeconds")] public int? CooldownStepSeconds { get; set; }
        [JsonPropertyName("cooldownCurve")] public SiegeShopCurve CooldownCurve { get; set; }
        [JsonPropertyName("cooldownGrowthFactor")] public double? CooldownGrowthFactor { get; set; }
        [JsonPropertyName("cooldownMaxSeconds")] public int? CooldownMaxSeconds { get; set; }
        [JsonPropertyName("maxPerMatch")] public int? MaxPerMatch { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
    }
}
