using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    // Lootbox runtime DTOs (knk-workspace docs/specs/lootboxes/DESIGN.md §3.3, Phase 2): what knk-plugin reads and
    // sends while boxes spawn, get claimed and are delivered, plus the web app's drop-log rows. All times are UTC.

    // ===== Runtime config (GET api/LootboxSpawns/runtime-config) =====

    public class LootboxRuntimeConfigDto
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("globalMaxActive")]
        public int GlobalMaxActive { get; set; }

        // Per player per UTC calendar day, all types together; null = no cap.
        [JsonPropertyName("maxClaimsPerPlayerPerDay")]
        public int? MaxClaimsPerPlayerPerDay { get; set; }

        [JsonPropertyName("announceMinItemStars")]
        public int AnnounceMinItemStars { get; set; }

        [JsonPropertyName("announceSpawnMinBoxStars")]
        public int AnnounceSpawnMinBoxStars { get; set; }

        [JsonPropertyName("dropAnnouncementTemplate")]
        public string DropAnnouncementTemplate { get; set; } = string.Empty;

        [JsonPropertyName("spawnAnnouncementTemplate")]
        public string SpawnAnnouncementTemplate { get; set; } = string.Empty;

        [JsonPropertyName("serverTimeUtc")]
        public DateTime ServerTimeUtc { get; set; }

        // Enabled types only.
        [JsonPropertyName("types")]
        public List<LootboxRuntimeTypeDto> Types { get; set; } = new();

        // Every grade, so the plugin can name a box by its stars ("Legendary Weapons Lootbox").
        [JsonPropertyName("grades")]
        public List<LootboxRuntimeGradeDto> Grades { get; set; } = new();

        // All areas with their enabled flag: the scheduler skips disabled ones, /knk lootbox area list shows them.
        [JsonPropertyName("areas")]
        public List<LootboxRuntimeAreaDto> Areas { get; set; } = new();
    }

    public class LootboxRuntimeTypeDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("categoryId")]
        public int CategoryId { get; set; }

        [JsonPropertyName("categoryName")]
        public string CategoryName { get; set; } = string.Empty;

        // The type's display material, else the category icon, else minecraft:chest.
        [JsonPropertyName("displayMaterialKey")]
        public string DisplayMaterialKey { get; set; } = string.Empty;

        [JsonPropertyName("spawnWeight")]
        public int SpawnWeight { get; set; }

        [JsonPropertyName("minBoxStars")]
        public int MinBoxStars { get; set; }

        [JsonPropertyName("maxBoxStars")]
        public int MaxBoxStars { get; set; }

        [JsonPropertyName("maxClaimsPerPlayerPerDay")]
        public int? MaxClaimsPerPlayerPerDay { get; set; }

        // Null = the global AnnounceMinItemStars.
        [JsonPropertyName("announceMinItemStars")]
        public int? AnnounceMinItemStars { get; set; }
    }

    public class LootboxRuntimeGradeDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("stars")]
        public int Stars { get; set; }
    }

    public class LootboxRuntimeAreaDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("world")]
        public string World { get; set; } = string.Empty;

        [JsonPropertyName("wgRegionId")]
        public string WgRegionId { get; set; } = string.Empty;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("maxActive")]
        public int MaxActive { get; set; }

        [JsonPropertyName("spawnIntervalSeconds")]
        public int SpawnIntervalSeconds { get; set; }

        [JsonPropertyName("spawnChancePercent")]
        public decimal SpawnChancePercent { get; set; }

        [JsonPropertyName("minOnlinePlayers")]
        public int MinOnlinePlayers { get; set; }

        [JsonPropertyName("minDistanceFromPlayers")]
        public int MinDistanceFromPlayers { get; set; }

        [JsonPropertyName("lifetimeMinutes")]
        public int LifetimeMinutes { get; set; }

        [JsonPropertyName("excludedRegionIds")]
        public List<string> ExcludedRegionIds { get; set; } = new();

        // Empty = every enabled type.
        [JsonPropertyName("allowedTypeIds")]
        public List<int> AllowedTypeIds { get; set; } = new();

        [JsonPropertyName("activeCount")]
        public int ActiveCount { get; set; }

        [JsonPropertyName("createdByUserId")]
        public int? CreatedByUserId { get; set; }
    }

    // ===== Spawns =====

    public class LootboxSpawnDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("token")]
        public Guid Token { get; set; }

        [JsonPropertyName("lootboxTypeId")]
        public int LootboxTypeId { get; set; }

        [JsonPropertyName("lootboxTypeName")]
        public string LootboxTypeName { get; set; } = string.Empty;

        [JsonPropertyName("categoryName")]
        public string? CategoryName { get; set; }

        [JsonPropertyName("boxGradeId")]
        public int BoxGradeId { get; set; }

        [JsonPropertyName("boxGradeName")]
        public string BoxGradeName { get; set; } = string.Empty;

        [JsonPropertyName("boxStars")]
        public int BoxStars { get; set; }

        // "<GradeName> <TypeName>", e.g. "Legendary Weapons Lootbox"; the plugin adds colour and stars.
        [JsonPropertyName("boxLabel")]
        public string BoxLabel { get; set; } = string.Empty;

        [JsonPropertyName("spawnAreaId")]
        public int? SpawnAreaId { get; set; }

        [JsonPropertyName("spawnAreaName")]
        public string? SpawnAreaName { get; set; }

        [JsonPropertyName("world")]
        public string World { get; set; } = string.Empty;

        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        [JsonPropertyName("z")]
        public int Z { get; set; }

        // Active | Claimed | Expired | Removed
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("spawnedAt")]
        public DateTime SpawnedAt { get; set; }

        [JsonPropertyName("expiresAt")]
        public DateTime ExpiresAt { get; set; }

        [JsonPropertyName("claimedAt")]
        public DateTime? ClaimedAt { get; set; }

        [JsonPropertyName("claimedByUserId")]
        public int? ClaimedByUserId { get; set; }

        [JsonPropertyName("serverId")]
        public string? ServerId { get; set; }

        [JsonPropertyName("createdByUserId")]
        public int? CreatedByUserId { get; set; }
    }

    /// <summary>POST api/LootboxSpawns: the plugin found a spot in an area; the API decides whether a box may spawn
    /// there and what it is.</summary>
    public class LootboxSpawnRequestDto
    {
        [JsonPropertyName("areaId")]
        public int AreaId { get; set; }

        [JsonPropertyName("world")]
        public string World { get; set; } = string.Empty;

        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        [JsonPropertyName("z")]
        public int Z { get; set; }

        [JsonPropertyName("serverId")]
        public string? ServerId { get; set; }
    }

    /// <summary>POST api/LootboxSpawns/admin (<c>/knk lootbox spawn</c>): ignores caps and areas. The staff member
    /// comes from the plugin's X-Acting-User-Id header, never from the body.</summary>
    public class LootboxAdminSpawnRequestDto
    {
        [JsonPropertyName("typeId")]
        public int TypeId { get; set; }

        // Null = rolled from the type's box-grade weights.
        [JsonPropertyName("boxStars")]
        public int? BoxStars { get; set; }

        [JsonPropertyName("world")]
        public string World { get; set; } = string.Empty;

        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        [JsonPropertyName("z")]
        public int Z { get; set; }

        [JsonPropertyName("serverId")]
        public string? ServerId { get; set; }

        // Null = the default area lifetime (30 minutes).
        [JsonPropertyName("lifetimeMinutes")]
        public int? LifetimeMinutes { get; set; }
    }

    // ===== Claims =====

    public class LootboxClaimRequestDto
    {
        [JsonPropertyName("token")]
        public Guid Token { get; set; }

        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        // "{token}:{userId}" by convention; the same key replays the stored result instead of rolling again.
        [JsonPropertyName("idempotencyKey")]
        public string IdempotencyKey { get; set; } = string.Empty;
    }

    /// <summary>POST api/LootboxClaims/admin-give (<c>/knk lootbox give</c>): roll and mint without a world box. Not
    /// counted against the daily cap. The staff member comes from X-Acting-User-Id.</summary>
    public class LootboxAdminGiveRequestDto
    {
        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        [JsonPropertyName("typeId")]
        public int TypeId { get; set; }

        [JsonPropertyName("boxStars")]
        public int? BoxStars { get; set; }

        // Optional; a retried give with the same key replays the first result. Null = a fresh give every call.
        [JsonPropertyName("idempotencyKey")]
        public string? IdempotencyKey { get; set; }
    }

    public class LootboxClaimEnchantmentDto
    {
        [JsonPropertyName("definitionId")]
        public int DefinitionId { get; set; }

        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("isCustom")]
        public bool IsCustom { get; set; }

        [JsonPropertyName("level")]
        public int Level { get; set; }
    }

    /// <summary>
    /// The outcome of a claim or admin give (DESIGN.md §3.3), also returned by <c>pending</c> and on an idempotent
    /// replay (<see cref="Replay"/> = true, same payload). <see cref="Enchantments"/> is the item's final set: the
    /// minted instance's rows, or the blueprint defaults for a stackable item (no instance).
    /// </summary>
    public class LootboxClaimResultDto
    {
        [JsonPropertyName("claimId")]
        public int ClaimId { get; set; }

        [JsonPropertyName("replay")]
        public bool Replay { get; set; }

        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        // Null for an admin give.
        [JsonPropertyName("lootboxSpawnId")]
        public int? LootboxSpawnId { get; set; }

        [JsonPropertyName("lootboxTypeId")]
        public int LootboxTypeId { get; set; }

        [JsonPropertyName("boxStars")]
        public int BoxStars { get; set; }

        [JsonPropertyName("boxLabel")]
        public string BoxLabel { get; set; } = string.Empty;

        // Null for stackable items.
        [JsonPropertyName("itemInstanceId")]
        public long? ItemInstanceId { get; set; }

        [JsonPropertyName("itemBlueprintId")]
        public int ItemBlueprintId { get; set; }

        [JsonPropertyName("itemName")]
        public string ItemName { get; set; } = string.Empty;

        [JsonPropertyName("itemGradeId")]
        public int? ItemGradeId { get; set; }

        [JsonPropertyName("itemGradeStars")]
        public int? ItemGradeStars { get; set; }

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }

        [JsonPropertyName("isSpecial")]
        public bool IsSpecial { get; set; }

        [JsonPropertyName("enchantments")]
        public List<LootboxClaimEnchantmentDto> Enchantments { get; set; } = new();

        // A special or an item of at least the announce threshold: the plugin broadcasts it once.
        [JsonPropertyName("announce")]
        public bool Announce { get; set; }

        [JsonPropertyName("claimedAt")]
        public DateTime ClaimedAt { get; set; }

        [JsonPropertyName("deliveredAt")]
        public DateTime? DeliveredAt { get; set; }
    }

    /// <summary>POST api/LootboxClaims/{id}/delivered.</summary>
    public class LootboxDeliveredRequestDto
    {
        // Inventory | DroppedOwned | Redelivered
        [JsonPropertyName("method")]
        public string Method { get; set; } = string.Empty;

        [JsonPropertyName("note")]
        public string? Note { get; set; }

        // Optional ownership check: when set it must be the claim's user.
        [JsonPropertyName("userId")]
        public int? UserId { get; set; }
    }

    public class LootboxDeliveredResultDto
    {
        [JsonPropertyName("claimId")]
        public int ClaimId { get; set; }

        [JsonPropertyName("deliveredAt")]
        public DateTime DeliveredAt { get; set; }

        [JsonPropertyName("deliveryMethod")]
        public string DeliveryMethod { get; set; } = string.Empty;

        // True when an earlier call already confirmed it; the first confirmation is kept.
        [JsonPropertyName("alreadyDelivered")]
        public bool AlreadyDelivered { get; set; }
    }

    /// <summary>One drop-log row for the web app (POST api/LootboxClaims/search).</summary>
    public class LootboxClaimLogDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("lootboxSpawnId")]
        public int? LootboxSpawnId { get; set; }

        // No world box: an admin give.
        [JsonPropertyName("isAdminGive")]
        public bool IsAdminGive { get; set; }

        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("lootboxTypeId")]
        public int LootboxTypeId { get; set; }

        [JsonPropertyName("lootboxTypeName")]
        public string? LootboxTypeName { get; set; }

        [JsonPropertyName("boxGradeId")]
        public int BoxGradeId { get; set; }

        [JsonPropertyName("boxStars")]
        public int? BoxStars { get; set; }

        [JsonPropertyName("itemBlueprintId")]
        public int ItemBlueprintId { get; set; }

        [JsonPropertyName("itemName")]
        public string? ItemName { get; set; }

        [JsonPropertyName("itemGradeId")]
        public int? ItemGradeId { get; set; }

        [JsonPropertyName("itemStars")]
        public int? ItemStars { get; set; }

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }

        [JsonPropertyName("isSpecial")]
        public bool IsSpecial { get; set; }

        [JsonPropertyName("itemInstanceId")]
        public long? ItemInstanceId { get; set; }

        [JsonPropertyName("claimedAt")]
        public DateTime ClaimedAt { get; set; }

        [JsonPropertyName("deliveredAt")]
        public DateTime? DeliveredAt { get; set; }

        [JsonPropertyName("deliveryMethod")]
        public string? DeliveryMethod { get; set; }

        [JsonPropertyName("deliveryNote")]
        public string? DeliveryNote { get; set; }
    }

    // ===== In-game spawn areas (/knk lootbox area create|delete) =====

    public class LootboxInGameAreaCreateDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("world")]
        public string World { get; set; } = string.Empty;

        [JsonPropertyName("wgRegionId")]
        public string WgRegionId { get; set; } = string.Empty;
    }

    public class LootboxInGameAreaDeleteResultDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("world")]
        public string World { get; set; } = string.Empty;

        // The plugin removes the WG region only when it starts with lootbox_ (DESIGN.md §3.4).
        [JsonPropertyName("wgRegionId")]
        public string WgRegionId { get; set; } = string.Empty;

        // The boxes that were active in the area and are now Removed; the plugin removes their entities.
        [JsonPropertyName("removedSpawnIds")]
        public List<int> RemovedSpawnIds { get; set; } = new();
    }
}
