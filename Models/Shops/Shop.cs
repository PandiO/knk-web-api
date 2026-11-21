using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// Shops P10 (PandiO/knk-workspace#248, shops DESIGN § 2-5, § 9.1; production DESIGN § 10). The static admin catalogue,
// players buying (coins to the town account) and selling, interim pricing, shop levels, restocking through the town
// planner with the shop's own worker, processing lines on the P3 engine, and hourly trade stats. Restocking, processing,
// player selling and the stats job need Production:Enabled; admin CRUD and buying from the catalogue work while it is off.

/// <summary>
/// A shop (shops DESIGN § 2.1): a structure with an Input and a Merchandise storage (created with it), a kind, a shop
/// type (accepted categories and levels), pricing modifiers and a shopkeeper spawn location (P11).
/// <para>
/// A TPT subtype of <see cref="ProductionStructure"/> (table <c>shops</c>), so a shop has the producer's own worker
/// (<see cref="ProductionStructure.WorkersTotal"/>, collected by the town planner, TOWN_LOGISTICS § 5), carries goods as a
/// transport carrier and runs processing lines on the production engine (Input → Merchandise). Its level is
/// <see cref="ProductionStructure.Level"/>, read from <see cref="ShopTypeLevel"/> rows (not the production kind levels);
/// <see cref="ProductionStructure.ProductionKindId"/> stays empty.
/// </para>
/// </summary>
[FormConfigurableEntity("Shop")]
public class Shop : ProductionStructure
{
    public const decimal DefaultBuyBackPercent = 50m;
    public const decimal MaxTaxModifier = 100m;

    /// <summary>The shop type (bakery, weaponsmith, …): accepted categories and levels. Null = none chosen yet (the shop
    /// then buys nothing from players and uses level multiplier 1).</summary>
    [NavigationPair(nameof(ShopType))]
    [RelatedEntityField(typeof(ShopType))]
    public int? ShopTypeId { get; set; }
    [RelatedEntityField(typeof(ShopType))]
    public ShopType? ShopType { get; set; }

    public ShopKind Kind { get; set; } = ShopKind.Trading;

    /// <summary>The interim stand-in for a town tax (shops DESIGN § 4): sell price × this. 1 = no change.</summary>
    public decimal TaxModifier { get; set; } = 1m;

    /// <summary>Buy-back price = sell price × this / 100 (default 50).</summary>
    public decimal BuyBackPercent { get; set; } = DefaultBuyBackPercent;

    /// <summary>Where the shopkeeper NPC spawns (P11, shops DESIGN § 6).</summary>
    [NavigationPair(nameof(ShopkeeperLocation))]
    [RelatedEntityField(typeof(Location))]
    public int? ShopkeeperLocationId { get; set; }
    [RelatedEntityField(typeof(Location))]
    public Location? ShopkeeperLocation { get; set; }

    /// <summary>Hide sold-out items instead of greying them out (F5; display stalls P12 and the menus).</summary>
    public bool HideSoldOut { get; set; }

    [RelatedEntityField(typeof(ShopCatalogueEntry))]
    public ICollection<ShopCatalogueEntry> CatalogueEntries { get; set; } = new List<ShopCatalogueEntry>();
}

/// <summary>A shop type (shops DESIGN § 2.1, § 3.2): its accepted categories (a weaponsmith buys weapons only; a
/// category accepts its sub-categories) and its levels (§ 5).</summary>
[FormConfigurableEntity("ShopType")]
public class ShopType
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    public int Id { get; set; }

    [MaxLength(NameMaxLength)]
    public string Name { get; set; } = null!;

    [MaxLength(DescriptionMaxLength)]
    public string? Description { get; set; }

    /// <summary>The kind a new shop of this type gets when none is given.</summary>
    public ShopKind DefaultKind { get; set; } = ShopKind.Trading;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [RelatedEntityField(typeof(ShopTypeCategory))]
    public ICollection<ShopTypeCategory> AcceptedCategories { get; set; } = new List<ShopTypeCategory>();

    [RelatedEntityField(typeof(ShopTypeLevel))]
    public ICollection<ShopTypeLevel> Levels { get; set; } = new List<ShopTypeLevel>();
}

/// <summary>A category a shop type buys from players (join row; edited through the shop type's
/// <c>acceptedCategoryIds</c>).</summary>
public class ShopTypeCategory
{
    public int Id { get; set; }

    public int ShopTypeId { get; set; }
    public ShopType ShopType { get; set; } = null!;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
}

/// <summary>
/// One level of a shop type (shops DESIGN § 5, the production level pattern of PRODUCTION_KINDS § 5): processing speed
/// and yield, input cost, the price multiplier (§ 4), storage caps and the default reorder point and target level for
/// restocking (an admin's consumer replenishment setting overrides them). The number of levels is free.
/// </summary>
[FormConfigurableEntity("ShopTypeLevel")]
public class ShopTypeLevel
{
    public const int MaxLevel = 100;

    public int Id { get; set; }

    [NavigationPair(nameof(ShopType))]
    [RelatedEntityField(typeof(ShopType))]
    public int ShopTypeId { get; set; }
    [RelatedEntityField(typeof(ShopType))]
    public ShopType ShopType { get; set; } = null!;

    public int Level { get; set; } = 1;

    /// <summary>Processing cycle speed (cycle length ÷ this).</summary>
    public decimal CycleSpeedMultiplier { get; set; } = 1m;

    /// <summary>Processing yield (output rate × this).</summary>
    public decimal YieldMultiplier { get; set; } = 1m;

    /// <summary>Input cost per cycle (inputs per hour × this).</summary>
    public decimal InputCostMultiplier { get; set; } = 1m;

    /// <summary>The shop level modifier of the sell price (§ 4).</summary>
    public decimal PriceMultiplier { get; set; } = 1m;

    /// <summary>Merchandise storage cap in raw units; null leaves the current cap.</summary>
    public long? MerchandiseMaxUnits { get; set; }

    /// <summary>Input storage cap in raw units; null leaves the current cap.</summary>
    public long? InputMaxUnits { get; set; }

    /// <summary>Raw units per bound container slot (both storages); null leaves the current value.</summary>
    public int? UnitsPerSlot { get; set; }

    /// <summary>Default restocking reorder point per catalogue blueprint (units in Merchandise); null = no default.</summary>
    public long? ReorderPointUnits { get; set; }

    /// <summary>Default restocking target level per catalogue blueprint; null = no default.</summary>
    public long? TargetUnits { get; set; }
}

/// <summary>
/// One row of a shop's static admin catalogue (shops DESIGN § 2.3, § 4): a blueprint or one item instance, with an
/// optional static price that wins over the formula, an admin pin (display stalls, P12) and an order. Goods come from
/// Merchandise; an <see cref="ShopStockMode.Unlimited"/> blueprint entry never sells out (the fallback before the supply
/// chain is live). Bought-back items and restocked goods without an entry are on sale at the formula price.
/// </summary>
[FormConfigurableEntity("ShopCatalogueEntry")]
public class ShopCatalogueEntry
{
    public int Id { get; set; }

    [NavigationPair(nameof(Shop))]
    [RelatedEntityField(typeof(Shop))]
    public int ShopId { get; set; }
    [RelatedEntityField(typeof(Shop))]
    public Shop Shop { get; set; } = null!;

    /// <summary>Set for a stackable item; the instance's blueprint for a unique item.</summary>
    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    /// <summary>Set for one unique item (sold once, from Merchandise).</summary>
    [NavigationPair(nameof(ItemInstance))]
    [RelatedEntityField(typeof(ItemInstance))]
    public long? ItemInstanceId { get; set; }
    [RelatedEntityField(typeof(ItemInstance))]
    public ItemInstance? ItemInstance { get; set; }

    /// <summary>Coins per unit; wins over the formula. Null = the formula price.</summary>
    public long? StaticPrice { get; set; }

    public ShopStockMode StockMode { get; set; } = ShopStockMode.Stocked;

    /// <summary>Shown first on display stalls (P12, shops DESIGN § 7).</summary>
    public bool Pinned { get; set; }

    public int SortOrder { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Hourly trade stats per shop, blueprint and UTC hour (shops DESIGN § 9.1), rolled up from the stock and currency ledger
/// so they can be rebuilt at any time. Recorded for the later supply-and-demand pricing; V1 pricing reads none of it.
/// Hours with no trade have no row.
/// </summary>
[FormConfigurableEntity("ShopTradeStat")]
public class ShopTradeStat
{
    public long Id { get; set; }

    [NavigationPair(nameof(Shop))]
    [RelatedEntityField(typeof(Shop))]
    public int ShopId { get; set; }
    [RelatedEntityField(typeof(Shop))]
    public Shop Shop { get; set; } = null!;

    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    /// <summary>The UTC hour (minutes and seconds zero).</summary>
    public DateTime HourStart { get; set; }

    public long UnitsSold { get; set; }
    public long Revenue { get; set; }
    public long UnitsBoughtBack { get; set; }
    public long BuyBackPaid { get; set; }

    /// <summary>Collections from the warehouse and processing output into Merchandise.</summary>
    public long UnitsSupplied { get; set; }

    public int DistinctCustomers { get; set; }

    /// <summary>The town's player count at the end of the hour; null while towns have no member count (V1).</summary>
    public int? TownPlayerCount { get; set; }

    /// <summary>Revenue ÷ units sold; null without sales.</summary>
    public decimal? AvgSellPrice { get; set; }

    /// <summary>Units of the blueprint in Merchandise at the end of the hour.</summary>
    public long StockAtEnd { get; set; }

    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A town's account (currency <see cref="CurrencyAccountKind.Town"/>, TOWN_LOGISTICS D-L5, shops DESIGN § 3.1): the
/// balance shop income goes to and buy-backs are paid from. Changed only through the currency ledger (town legs record
/// the balance before and after); created with the first posting. Trade, tax and spending from it are Later (#124).
/// </summary>
[FormConfigurableEntity("TownAccount")]
public class TownAccount
{
    public int Id { get; set; }

    [NavigationPair(nameof(Town))]
    [RelatedEntityField(typeof(Town))]
    public int TownId { get; set; }
    [RelatedEntityField(typeof(Town))]
    public Town Town { get; set; } = null!;

    public long Coins { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
