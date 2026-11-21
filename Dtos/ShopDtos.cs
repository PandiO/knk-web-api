using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    // Shops P10 (PandiO/knk-workspace#248, shops DESIGN § 2-5, § 9.1). On create/update a null field means "keep"
    // (create: the default). Enum values are their names ("Trading", "Processing", ...).

    /// <summary>The shops section of the game settings (<c>production.shops</c>, shops DESIGN § 4).</summary>
    public class ProductionShopSettingsDto
    {
        /// <summary>A unique item's price rises by this percentage of the base price per enchantment level (F3).</summary>
        [JsonPropertyName("enchantmentPercentPerLevel")] public decimal EnchantmentPercentPerLevel { get; set; } = 10m;

        /// <summary>The hourly trade-stats rollup also recomputes this many earlier hours (late ledger rows).</summary>
        [JsonPropertyName("statsLookbackHours")] public int StatsLookbackHours { get; set; } = 2;
    }

    // ================= Shop types and levels =================

    public class ShopTypeDto
    {
        [JsonPropertyName("id")] public int? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        /// <summary>Trading or Processing.</summary>
        [JsonPropertyName("defaultKind")] public string? DefaultKind { get; set; }
        [JsonPropertyName("isActive")] public bool? IsActive { get; set; }
        /// <summary>The categories the type buys from players; a category also accepts its sub-categories. On update
        /// the list replaces the current one; null keeps it.</summary>
        [JsonPropertyName("acceptedCategoryIds")] public List<int>? AcceptedCategoryIds { get; set; }
        [JsonPropertyName("acceptedCategories")] public List<NamedRefDto>? AcceptedCategories { get; set; }
        [JsonPropertyName("levels")] public List<ShopTypeLevelDto>? Levels { get; set; }
        [JsonPropertyName("createdAt")] public DateTime? CreatedAt { get; set; }
    }

    public class ShopTypeListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("defaultKind")] public string DefaultKind { get; set; } = string.Empty;
        [JsonPropertyName("isActive")] public bool IsActive { get; set; }
        [JsonPropertyName("acceptedCategoryCount")] public int AcceptedCategoryCount { get; set; }
        [JsonPropertyName("levelCount")] public int LevelCount { get; set; }
    }

    public class ShopTypeLevelDto
    {
        [JsonPropertyName("id")] public int? Id { get; set; }
        [JsonPropertyName("shopTypeId")] public int? ShopTypeId { get; set; }
        [JsonPropertyName("shopType")] public NamedRefDto? ShopType { get; set; }
        [JsonPropertyName("level")] public int? Level { get; set; }
        [JsonPropertyName("cycleSpeedMultiplier")] public decimal? CycleSpeedMultiplier { get; set; }
        [JsonPropertyName("yieldMultiplier")] public decimal? YieldMultiplier { get; set; }
        [JsonPropertyName("inputCostMultiplier")] public decimal? InputCostMultiplier { get; set; }
        [JsonPropertyName("priceMultiplier")] public decimal? PriceMultiplier { get; set; }
        /// <summary>0 clears it.</summary>
        [JsonPropertyName("merchandiseMaxUnits")] public long? MerchandiseMaxUnits { get; set; }
        /// <summary>0 clears it.</summary>
        [JsonPropertyName("inputMaxUnits")] public long? InputMaxUnits { get; set; }
        /// <summary>0 clears it.</summary>
        [JsonPropertyName("unitsPerSlot")] public int? UnitsPerSlot { get; set; }
        /// <summary>0 clears it.</summary>
        [JsonPropertyName("reorderPointUnits")] public long? ReorderPointUnits { get; set; }
        /// <summary>0 clears it.</summary>
        [JsonPropertyName("targetUnits")] public long? TargetUnits { get; set; }
    }

    public class ShopTypeLevelListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("shopTypeId")] public int ShopTypeId { get; set; }
        [JsonPropertyName("shopTypeName")] public string? ShopTypeName { get; set; }
        [JsonPropertyName("level")] public int Level { get; set; }
        [JsonPropertyName("priceMultiplier")] public decimal PriceMultiplier { get; set; }
        [JsonPropertyName("cycleSpeedMultiplier")] public decimal CycleSpeedMultiplier { get; set; }
        [JsonPropertyName("yieldMultiplier")] public decimal YieldMultiplier { get; set; }
        [JsonPropertyName("merchandiseMaxUnits")] public long? MerchandiseMaxUnits { get; set; }
    }

    // ================= Shops =================

    /// <summary>A shop: <c>api/Shops</c>. Carries every Structure field (placement as for any structure).</summary>
    public class ShopDto : StructureDto
    {
        [JsonPropertyName("shopTypeId")] public int? ShopTypeId { get; set; }
        [JsonPropertyName("shopType")] public NamedRefDto? ShopType { get; set; }
        /// <summary>Trading or Processing; on create without one, the shop type's default kind.</summary>
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("level")] public int? Level { get; set; }
        [JsonPropertyName("taxModifier")] public decimal? TaxModifier { get; set; }
        [JsonPropertyName("buyBackPercent")] public decimal? BuyBackPercent { get; set; }
        /// <summary>0 clears it.</summary>
        [JsonPropertyName("shopkeeperLocationId")] public int? ShopkeeperLocationId { get; set; }
        [JsonPropertyName("hideSoldOut")] public bool? HideSoldOut { get; set; }
        /// <summary>The shop's own processing switch (production engine).</summary>
        [JsonPropertyName("productionEnabled")] public bool? ProductionEnabled { get; set; }
        /// <summary>The shop's own worker(s) (restocking collects with them, processing slows while they are away).</summary>
        [JsonPropertyName("workersTotal")] public int? WorkersTotal { get; set; }
        /// <summary>Read only.</summary>
        [JsonPropertyName("workersPresent")] public int? WorkersPresent { get; set; }
        /// <summary>Raw units the worker carries per collection; 0 clears it (the planner's default).</summary>
        [JsonPropertyName("transportCapacity")] public int? TransportCapacity { get; set; }
        /// <summary>Read only: the Input and Merchandise storages (created with the shop).</summary>
        [JsonPropertyName("storages")] public List<StructureStorageNavDto>? Storages { get; set; }
        /// <summary>Read only: the town whose account the shop's income goes to.</summary>
        [JsonPropertyName("effectiveTownId")] public int? EffectiveTownId { get; set; }
        [JsonPropertyName("catalogueEntryCount")] public int? CatalogueEntryCount { get; set; }
    }

    // ================= Catalogue =================

    public class ShopCatalogueEntryDto
    {
        [JsonPropertyName("id")] public int? Id { get; set; }
        [JsonPropertyName("shopId")] public int? ShopId { get; set; }
        [JsonPropertyName("shop")] public NamedRefDto? Shop { get; set; }
        /// <summary>Required for a stackable item; set from the instance for a unique item.</summary>
        [JsonPropertyName("itemBlueprintId")] public int? ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprint")] public NamedRefDto? ItemBlueprint { get; set; }
        [JsonPropertyName("itemInstanceId")] public long? ItemInstanceId { get; set; }
        /// <summary>Coins per unit; wins over the formula. 0 clears it (the formula price).</summary>
        [JsonPropertyName("staticPrice")] public long? StaticPrice { get; set; }
        /// <summary>Stocked (from Merchandise) or Unlimited (blueprints only; never sold out).</summary>
        [JsonPropertyName("stockMode")] public string? StockMode { get; set; }
        [JsonPropertyName("pinned")] public bool? Pinned { get; set; }
        [JsonPropertyName("sortOrder")] public int? SortOrder { get; set; }
        [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
        [JsonPropertyName("createdAt")] public DateTime? CreatedAt { get; set; }
    }

    public class ShopCatalogueEntryListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("shopId")] public int ShopId { get; set; }
        [JsonPropertyName("shopName")] public string? ShopName { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("itemInstanceId")] public long? ItemInstanceId { get; set; }
        [JsonPropertyName("staticPrice")] public long? StaticPrice { get; set; }
        [JsonPropertyName("stockMode")] public string StockMode { get; set; } = string.Empty;
        [JsonPropertyName("pinned")] public bool Pinned { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    }

    // ================= Display spots (display stalls P12, #250) =================

    /// <summary>A shop's display spot: a block where the game server shows one of the shop's items.</summary>
    public class ShopDisplaySpotDto
    {
        [JsonPropertyName("id")] public int? Id { get; set; }
        [JsonPropertyName("shopId")] public int? ShopId { get; set; }
        [JsonPropertyName("shop")] public NamedRefDto? Shop { get; set; }
        [JsonPropertyName("worldName")] public string? WorldName { get; set; }
        [JsonPropertyName("x")] public int? X { get; set; }
        [JsonPropertyName("y")] public int? Y { get; set; }
        [JsonPropertyName("z")] public int? Z { get; set; }
        /// <summary>North, East, South or West (default South).</summary>
        [JsonPropertyName("facing")] public string? Facing { get; set; }
        /// <summary>A catalogue entry of the same shop this spot always shows; 0 clears it.</summary>
        [JsonPropertyName("pinnedCatalogueEntryId")] public int? PinnedCatalogueEntryId { get; set; }
        /// <summary>Read only: the pinned entry's item.</summary>
        [JsonPropertyName("pinnedCatalogueEntry")] public NamedRefDto? PinnedCatalogueEntry { get; set; }
        [JsonPropertyName("sortOrder")] public int? SortOrder { get; set; }
        /// <summary>An optional name for staff; empty clears it.</summary>
        [JsonPropertyName("label")] public string? Label { get; set; }
        [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
        [JsonPropertyName("createdByUserId")] public int? CreatedByUserId { get; set; }
        [JsonPropertyName("createdAt")] public DateTime? CreatedAt { get; set; }
        [JsonPropertyName("updatedAt")] public DateTime? UpdatedAt { get; set; }
    }

    public class ShopDisplaySpotListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("shopId")] public int ShopId { get; set; }
        [JsonPropertyName("shopName")] public string? ShopName { get; set; }
        [JsonPropertyName("worldName")] public string WorldName { get; set; } = string.Empty;
        [JsonPropertyName("x")] public int X { get; set; }
        [JsonPropertyName("y")] public int Y { get; set; }
        [JsonPropertyName("z")] public int Z { get; set; }
        [JsonPropertyName("facing")] public string Facing { get; set; } = string.Empty;
        [JsonPropertyName("pinnedCatalogueEntryId")] public int? PinnedCatalogueEntryId { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        [JsonPropertyName("label")] public string? Label { get; set; }
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    }

    /// <summary>
    /// What a shop offers now (the shop menu, display stalls, the web app): its catalogue entries and what is in its
    /// Merchandise storage without an entry (restocked goods, bought-back items), each with its current price and stock.
    /// Sold-out entries are listed with <c>soldOut</c> unless the shop hides them.
    /// </summary>
    public class ShopOfferDto
    {
        [JsonPropertyName("shopId")] public int ShopId { get; set; }
        [JsonPropertyName("shopName")] public string ShopName { get; set; } = string.Empty;
        [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
        [JsonPropertyName("level")] public int Level { get; set; }
        [JsonPropertyName("shopTypeName")] public string? ShopTypeName { get; set; }
        [JsonPropertyName("townId")] public int? TownId { get; set; }
        [JsonPropertyName("townName")] public string? TownName { get; set; }
        [JsonPropertyName("hideSoldOut")] public bool HideSoldOut { get; set; }
        /// <summary>Whether players can sell to the shop now (Production:Enabled and a shop type with categories).</summary>
        [JsonPropertyName("buysFromPlayers")] public bool BuysFromPlayers { get; set; }
        [JsonPropertyName("items")] public List<ShopOfferItemDto> Items { get; set; } = new();
    }

    public class ShopOfferItemDto
    {
        /// <summary>The catalogue entry; null for Merchandise stock without an entry.</summary>
        [JsonPropertyName("catalogueEntryId")] public int? CatalogueEntryId { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string ItemBlueprintName { get; set; } = string.Empty;
        /// <summary>Set for one unique item (bought-back items are resold as that exact item).</summary>
        [JsonPropertyName("itemInstanceId")] public long? ItemInstanceId { get; set; }
        [JsonPropertyName("unitPrice")] public long? UnitPrice { get; set; }
        /// <summary>Static, Formula or None (no price: not for sale).</summary>
        [JsonPropertyName("priceSource")] public string PriceSource { get; set; } = string.Empty;
        /// <summary>Units on sale; null = unlimited.</summary>
        [JsonPropertyName("stock")] public long? Stock { get; set; }
        [JsonPropertyName("soldOut")] public bool SoldOut { get; set; }
        [JsonPropertyName("pinned")] public bool Pinned { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        /// <summary>Enchantments of a unique item, "name level".</summary>
        [JsonPropertyName("enchantments")] public List<string>? Enchantments { get; set; }
    }

    // ================= Buying and selling =================

    /// <summary>
    /// A purchase (shops DESIGN § 3.1), sent by the game server for a player after the player confirmed it. The API needs
    /// <see cref="Confirmed"/> = true and the <see cref="ExpectedUnitPrice"/> the player saw: a changed price is refused
    /// (409 PriceChanged) so the player confirms again.
    /// </summary>
    public class ShopPurchaseRequestDto
    {
        [JsonPropertyName("userId")] public int UserId { get; set; }
        /// <summary>A catalogue entry, or a blueprint / unique item from the offer without an entry.</summary>
        [JsonPropertyName("catalogueEntryId")] public int? CatalogueEntryId { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int? ItemBlueprintId { get; set; }
        [JsonPropertyName("itemInstanceId")] public long? ItemInstanceId { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; } = 1;
        [JsonPropertyName("expectedUnitPrice")] public long? ExpectedUnitPrice { get; set; }
        [JsonPropertyName("confirmed")] public bool Confirmed { get; set; }
        /// <summary>Inventory (the game server places the item; it checked the room) or Inbox (P7 delivery; needs
        /// Production:Enabled).</summary>
        [JsonPropertyName("deliverTo")] public string? DeliverTo { get; set; }
        /// <summary>Required: a retry with the same key applies once.</summary>
        [JsonPropertyName("idempotencyKey")] public string? IdempotencyKey { get; set; }
    }

    /// <summary>A quote: what a purchase would cost now (no change).</summary>
    public class ShopQuoteDto
    {
        [JsonPropertyName("shopId")] public int ShopId { get; set; }
        [JsonPropertyName("catalogueEntryId")] public int? CatalogueEntryId { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string ItemBlueprintName { get; set; } = string.Empty;
        [JsonPropertyName("itemInstanceId")] public long? ItemInstanceId { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; }
        [JsonPropertyName("unitPrice")] public long UnitPrice { get; set; }
        [JsonPropertyName("totalPrice")] public long TotalPrice { get; set; }
        [JsonPropertyName("priceSource")] public string PriceSource { get; set; } = string.Empty;
        [JsonPropertyName("stock")] public long? Stock { get; set; }
        /// <summary>Null when the quote can be bought; else why not (SoldOut, NotEnoughStock, NotForSale, ...).</summary>
        [JsonPropertyName("refusal")] public string? Refusal { get; set; }
        [JsonPropertyName("refusalMessage")] public string? RefusalMessage { get; set; }
    }

    public class ShopPurchaseResultDto
    {
        [JsonPropertyName("shopId")] public int ShopId { get; set; }
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemInstanceId")] public long? ItemInstanceId { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; }
        [JsonPropertyName("unitPrice")] public long UnitPrice { get; set; }
        [JsonPropertyName("totalPrice")] public long TotalPrice { get; set; }
        [JsonPropertyName("deliverTo")] public string DeliverTo { get; set; } = string.Empty;
        [JsonPropertyName("stockTransactionId")] public long? StockTransactionId { get; set; }
        [JsonPropertyName("currencyTransactionId")] public string? CurrencyTransactionId { get; set; }
        [JsonPropertyName("itemDeliveryId")] public long? ItemDeliveryId { get; set; }
        [JsonPropertyName("townId")] public int TownId { get; set; }
        [JsonPropertyName("coinsAfter")] public long? CoinsAfter { get; set; }
        [JsonPropertyName("replayed")] public bool Replayed { get; set; }
    }

    /// <summary>A player sells items to a shop (shops DESIGN § 3.2), sent by the game server after the player confirmed
    /// the sell menu. Each line is a stackable blueprint and amount or one unique item.</summary>
    public class ShopSellRequestDto
    {
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("lines")] public List<ShopSellLineDto> Lines { get; set; } = new();
        /// <summary>The total the player saw; a changed total is refused (409 PriceChanged).</summary>
        [JsonPropertyName("expectedTotal")] public long? ExpectedTotal { get; set; }
        [JsonPropertyName("confirmed")] public bool Confirmed { get; set; }
        [JsonPropertyName("idempotencyKey")] public string? IdempotencyKey { get; set; }
    }

    public class ShopSellLineDto
    {
        [JsonPropertyName("itemBlueprintId")] public int? ItemBlueprintId { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; } = 1;
        [JsonPropertyName("itemInstanceId")] public long? ItemInstanceId { get; set; }
    }

    /// <summary>The sell menu's prices: per line the buy-back price or the refusal, and the total of the accepted lines.</summary>
    public class ShopSellQuoteDto
    {
        [JsonPropertyName("shopId")] public int ShopId { get; set; }
        [JsonPropertyName("lines")] public List<ShopSellQuoteLineDto> Lines { get; set; } = new();
        [JsonPropertyName("total")] public long Total { get; set; }
        /// <summary>Whether every line is accepted (a sale is all or nothing).</summary>
        [JsonPropertyName("accepted")] public bool Accepted { get; set; }
        [JsonPropertyName("townCanPay")] public bool TownCanPay { get; set; }
    }

    public class ShopSellQuoteLineDto
    {
        [JsonPropertyName("lineNo")] public int LineNo { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int? ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("itemInstanceId")] public long? ItemInstanceId { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; }
        [JsonPropertyName("unitPrice")] public long UnitPrice { get; set; }
        [JsonPropertyName("linePrice")] public long LinePrice { get; set; }
        /// <summary>Null = accepted; else CategoryNotAccepted, NoPrice, NotResolved, NoRoom, InStorage.</summary>
        [JsonPropertyName("refusal")] public string? Refusal { get; set; }
        [JsonPropertyName("refusalMessage")] public string? RefusalMessage { get; set; }
    }

    public class ShopSellResultDto
    {
        [JsonPropertyName("shopId")] public int ShopId { get; set; }
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("total")] public long Total { get; set; }
        [JsonPropertyName("lines")] public List<ShopSellQuoteLineDto> Lines { get; set; } = new();
        [JsonPropertyName("stockTransactionIds")] public List<long> StockTransactionIds { get; set; } = new();
        [JsonPropertyName("currencyTransactionIds")] public List<string> CurrencyTransactionIds { get; set; } = new();
        [JsonPropertyName("coinsAfter")] public long? CoinsAfter { get; set; }
        [JsonPropertyName("replayed")] public bool Replayed { get; set; }
    }

    // ================= Trade stats and the town account =================

    public class ShopTradeStatDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("shopId")] public int ShopId { get; set; }
        [JsonPropertyName("shop")] public NamedRefDto? Shop { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprint")] public NamedRefDto? ItemBlueprint { get; set; }
        [JsonPropertyName("hourStart")] public DateTime HourStart { get; set; }
        [JsonPropertyName("unitsSold")] public long UnitsSold { get; set; }
        [JsonPropertyName("revenue")] public long Revenue { get; set; }
        [JsonPropertyName("unitsBoughtBack")] public long UnitsBoughtBack { get; set; }
        [JsonPropertyName("buyBackPaid")] public long BuyBackPaid { get; set; }
        [JsonPropertyName("unitsSupplied")] public long UnitsSupplied { get; set; }
        [JsonPropertyName("distinctCustomers")] public int DistinctCustomers { get; set; }
        [JsonPropertyName("townPlayerCount")] public int? TownPlayerCount { get; set; }
        [JsonPropertyName("avgSellPrice")] public decimal? AvgSellPrice { get; set; }
        [JsonPropertyName("stockAtEnd")] public long StockAtEnd { get; set; }
        [JsonPropertyName("computedAt")] public DateTime ComputedAt { get; set; }
    }

    public class ShopTradeStatListDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("shopId")] public int ShopId { get; set; }
        [JsonPropertyName("shopName")] public string? ShopName { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("hourStart")] public DateTime HourStart { get; set; }
        [JsonPropertyName("unitsSold")] public long UnitsSold { get; set; }
        [JsonPropertyName("revenue")] public long Revenue { get; set; }
        [JsonPropertyName("unitsBoughtBack")] public long UnitsBoughtBack { get; set; }
        [JsonPropertyName("buyBackPaid")] public long BuyBackPaid { get; set; }
        [JsonPropertyName("unitsSupplied")] public long UnitsSupplied { get; set; }
        [JsonPropertyName("stockAtEnd")] public long StockAtEnd { get; set; }
    }

    /// <summary>Rebuild the hourly stats of a range (UTC hours, from inclusive, to exclusive), one shop or all.</summary>
    public class ShopTradeStatRebuildDto
    {
        [JsonPropertyName("shopId")] public int? ShopId { get; set; }
        [JsonPropertyName("from")] public DateTime From { get; set; }
        [JsonPropertyName("to")] public DateTime To { get; set; }
    }

    public class ShopTradeStatRebuildResultDto
    {
        [JsonPropertyName("hours")] public int Hours { get; set; }
        [JsonPropertyName("rowsWritten")] public int RowsWritten { get; set; }
        [JsonPropertyName("rowsDeleted")] public int RowsDeleted { get; set; }
    }

    public class TownAccountDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("townId")] public int TownId { get; set; }
        [JsonPropertyName("town")] public NamedRefDto? Town { get; set; }
        [JsonPropertyName("coins")] public long Coins { get; set; }
        /// <summary>Whether the last town leg's balance after equals <see cref="Coins"/> (the reconciliation).</summary>
        [JsonPropertyName("reconciled")] public bool Reconciled { get; set; }
        [JsonPropertyName("ledgerBalance")] public long? LedgerBalance { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; set; }
    }

    public class TownAccountListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("townId")] public int TownId { get; set; }
        [JsonPropertyName("townName")] public string? TownName { get; set; }
        [JsonPropertyName("coins")] public long Coins { get; set; }
        [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; set; }
    }

    /// <summary>One town leg with its transaction's context.</summary>
    public class TownAccountEntryDto
    {
        [JsonPropertyName("entryId")] public long EntryId { get; set; }
        [JsonPropertyName("transactionPublicId")] public string TransactionPublicId { get; set; } = string.Empty;
        [JsonPropertyName("reasonCode")] public string ReasonCode { get; set; } = string.Empty;
        [JsonPropertyName("amount")] public long Amount { get; set; }
        [JsonPropertyName("balanceBefore")] public long BalanceBefore { get; set; }
        [JsonPropertyName("balanceAfter")] public long BalanceAfter { get; set; }
        [JsonPropertyName("sourceType")] public string? SourceType { get; set; }
        [JsonPropertyName("sourceRef")] public string? SourceRef { get; set; }
        [JsonPropertyName("userId")] public int? UserId { get; set; }
        /// <summary>The player's username (#938), null when the leg has no user or the user is gone.</summary>
        [JsonPropertyName("username")] public string? Username { get; set; }
        /// <summary>The shop sale's or buy-back's stock transaction (#938): the goods leg of the same trade, joined on the
        /// shared idempotency key; null for any other leg.</summary>
        [JsonPropertyName("stockTransactionId")] public long? StockTransactionId { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
    }
}
