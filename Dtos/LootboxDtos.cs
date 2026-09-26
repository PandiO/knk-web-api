using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    // Lootbox admin configuration DTOs (knk-workspace docs/specs/lootboxes/DESIGN.md §3.2-§3.3, Phase 1).
    // Read-only navigation objects are filled on reads and ignored on create/update, as in KitDto.

    public class LootboxTypeDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [Required]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("categoryId")]
        public int CategoryId { get; set; }

        [JsonPropertyName("category")]
        public CategoryNavDto? Category { get; set; }

        [JsonPropertyName("includeSubcategories")]
        public bool IncludeSubcategories { get; set; } = true;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("spawnWeight")]
        public int SpawnWeight { get; set; } = 10;

        [JsonPropertyName("minBoxStars")]
        public int MinBoxStars { get; set; } = 1;

        [JsonPropertyName("maxBoxStars")]
        public int MaxBoxStars { get; set; } = 5;

        [JsonPropertyName("itemStarSpread")]
        public int ItemStarSpread { get; set; } = 2;

        [JsonPropertyName("displayMaterialRefId")]
        public int? DisplayMaterialRefId { get; set; }

        [JsonPropertyName("displayMaterial")]
        public MinecraftMaterialRefNavDto? DisplayMaterial { get; set; }

        [JsonPropertyName("maxClaimsPerPlayerPerDay")]
        public int? MaxClaimsPerPlayerPerDay { get; set; }

        [JsonPropertyName("announceMinItemStars")]
        public int? AnnounceMinItemStars { get; set; }

        [JsonPropertyName("gradeWeights")]
        public List<LootboxTypeGradeWeightDto> GradeWeights { get; set; } = new();

        [JsonPropertyName("poolEntries")]
        public List<LootboxPoolEntryDto> PoolEntries { get; set; } = new();

        [JsonPropertyName("enchantRolls")]
        public List<LootboxEnchantRollDto> EnchantRolls { get; set; } = new();
    }

    // Lightweight LootboxType shape for navigation fields (special entries, spawn areas).
    public class LootboxTypeNavDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class LootboxTypeGradeWeightDto
    {
        [JsonPropertyName("gradeId")]
        public int GradeId { get; set; }

        [JsonPropertyName("grade")]
        public GradeNavDto? Grade { get; set; }

        [JsonPropertyName("weight")]
        public decimal Weight { get; set; }
    }

    public class LootboxPoolEntryDto
    {
        [JsonPropertyName("itemBlueprintId")]
        public int ItemBlueprintId { get; set; }

        [JsonPropertyName("itemBlueprint")]
        public ItemBlueprintNavDto? ItemBlueprint { get; set; }

        // "Include" | "Exclude"
        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "Include";

        [JsonPropertyName("weightOverride")]
        public decimal? WeightOverride { get; set; }

        [JsonPropertyName("gradeIdOverride")]
        public int? GradeIdOverride { get; set; }

        [JsonPropertyName("gradeOverride")]
        public GradeNavDto? GradeOverride { get; set; }
    }

    public class LootboxEnchantRollDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("enchantmentDefinitionId")]
        public int EnchantmentDefinitionId { get; set; }

        [JsonPropertyName("enchantmentKey")]
        public string? EnchantmentKey { get; set; }

        [JsonPropertyName("chancePercent")]
        public decimal ChancePercent { get; set; }

        [JsonPropertyName("minLevel")]
        public int MinLevel { get; set; } = 1;

        [JsonPropertyName("maxLevel")]
        public int MaxLevel { get; set; } = 1;

        [JsonPropertyName("minBoxStars")]
        public int MinBoxStars { get; set; } = 1;

        [JsonPropertyName("sortOrder")]
        public int SortOrder { get; set; }
    }

    public class LootboxSpecialEntryDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        // null = any lootbox type.
        [JsonPropertyName("lootboxTypeId")]
        public int? LootboxTypeId { get; set; }

        [JsonPropertyName("lootboxType")]
        public LootboxTypeNavDto? LootboxType { get; set; }

        [JsonPropertyName("itemBlueprintId")]
        public int ItemBlueprintId { get; set; }

        [JsonPropertyName("itemBlueprint")]
        public ItemBlueprintNavDto? ItemBlueprint { get; set; }

        [JsonPropertyName("chancePerMillion")]
        public int ChancePerMillion { get; set; }

        [JsonPropertyName("minBoxStars")]
        public int MinBoxStars { get; set; } = 5;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("sortOrder")]
        public int SortOrder { get; set; }
    }

    public class LootboxSpawnAreaDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [Required]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [JsonPropertyName("world")]
        public string World { get; set; } = string.Empty;

        [Required]
        [JsonPropertyName("wgRegionId")]
        public string WgRegionId { get; set; } = string.Empty;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("maxActive")]
        public int MaxActive { get; set; } = 3;

        [JsonPropertyName("spawnIntervalSeconds")]
        public int SpawnIntervalSeconds { get; set; } = 600;

        [JsonPropertyName("spawnChancePercent")]
        public decimal SpawnChancePercent { get; set; } = 100m;

        [JsonPropertyName("minOnlinePlayers")]
        public int MinOnlinePlayers { get; set; } = 3;

        [JsonPropertyName("minDistanceFromPlayers")]
        public int MinDistanceFromPlayers { get; set; } = 24;

        [JsonPropertyName("lifetimeMinutes")]
        public int LifetimeMinutes { get; set; } = 30;

        // Comma-separated WG region ids.
        [JsonPropertyName("excludedRegionIds")]
        public string? ExcludedRegionIds { get; set; }

        // Read-only: set by the in-game command (Phase 2).
        [JsonPropertyName("createdByUserId")]
        public int? CreatedByUserId { get; set; }

        // Empty = every enabled type.
        [JsonPropertyName("allowedTypes")]
        public List<LootboxSpawnAreaTypeDto> AllowedTypes { get; set; } = new();
    }

    public class LootboxSpawnAreaTypeDto
    {
        [JsonPropertyName("lootboxTypeId")]
        public int LootboxTypeId { get; set; }

        [JsonPropertyName("lootboxType")]
        public LootboxTypeNavDto? LootboxType { get; set; }
    }

    public class LootboxConfigurationDto
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

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; }
    }

    public class UpdateLootboxConfigurationDto
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("globalMaxActive")]
        public int GlobalMaxActive { get; set; } = 15;

        [JsonPropertyName("maxClaimsPerPlayerPerDay")]
        public int? MaxClaimsPerPlayerPerDay { get; set; } = 10;

        [JsonPropertyName("announceMinItemStars")]
        public int AnnounceMinItemStars { get; set; } = 5;

        [JsonPropertyName("announceSpawnMinBoxStars")]
        public int AnnounceSpawnMinBoxStars { get; set; } = 6;

        [JsonPropertyName("dropAnnouncementTemplate")]
        public string? DropAnnouncementTemplate { get; set; }

        [JsonPropertyName("spawnAnnouncementTemplate")]
        public string? SpawnAnnouncementTemplate { get; set; }
    }

    // GET api/LootboxTypes/{id}/odds?boxStars= (DESIGN.md §3.3). Every percentage is 0-100. Grade percentages are
    // within the normal (non-special) roll; item and special percentages are of the whole box.
    public class LootboxOddsDto
    {
        [JsonPropertyName("lootboxTypeId")]
        public int LootboxTypeId { get; set; }

        [JsonPropertyName("lootboxTypeName")]
        public string LootboxTypeName { get; set; } = string.Empty;

        [JsonPropertyName("boxStars")]
        public int BoxStars { get; set; }

        // How often this type spawns as each box grade.
        [JsonPropertyName("boxGrades")]
        public List<LootboxGradeOddsDto> BoxGrades { get; set; } = new();

        [JsonPropertyName("specials")]
        public List<LootboxSpecialOddsDto> Specials { get; set; } = new();

        // Chance that no special hits and the normal roll runs.
        [JsonPropertyName("normalRollPercent")]
        public double NormalRollPercent { get; set; }

        // True when no grade in [boxStars - spread, boxStars] has items and a nearer grade is used instead.
        [JsonPropertyName("windowWidened")]
        public bool WindowWidened { get; set; }

        [JsonPropertyName("itemGrades")]
        public List<LootboxGradeOddsDto> ItemGrades { get; set; } = new();

        [JsonPropertyName("items")]
        public List<LootboxItemOddsDto> Items { get; set; } = new();

        [JsonPropertyName("enchantments")]
        public List<LootboxEnchantOddsDto> Enchantments { get; set; } = new();
    }

    public class LootboxGradeOddsDto
    {
        [JsonPropertyName("gradeId")]
        public int GradeId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("stars")]
        public int Stars { get; set; }

        [JsonPropertyName("percent")]
        public double Percent { get; set; }

        // Pool items of this grade (item grades only).
        [JsonPropertyName("itemCount")]
        public int? ItemCount { get; set; }
    }

    public class LootboxSpecialOddsDto
    {
        [JsonPropertyName("specialEntryId")]
        public int SpecialEntryId { get; set; }

        [JsonPropertyName("itemBlueprintId")]
        public int ItemBlueprintId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("chancePerMillion")]
        public int ChancePerMillion { get; set; }

        // The entry's own chance (chancePerMillion as a percentage).
        [JsonPropertyName("chancePercent")]
        public double ChancePercent { get; set; }

        // Chance that this special is what the box gives: its own chance times "every special before it missed".
        [JsonPropertyName("percent")]
        public double Percent { get; set; }
    }

    public class LootboxItemOddsDto
    {
        [JsonPropertyName("itemBlueprintId")]
        public int ItemBlueprintId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("gradeId")]
        public int GradeId { get; set; }

        [JsonPropertyName("stars")]
        public int Stars { get; set; }

        [JsonPropertyName("weight")]
        public decimal Weight { get; set; }

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }

        // False for books and stackable items: they never get rolled enchantments.
        [JsonPropertyName("rollsEnchantments")]
        public bool RollsEnchantments { get; set; }

        [JsonPropertyName("percentWithinGrade")]
        public double PercentWithinGrade { get; set; }

        [JsonPropertyName("percent")]
        public double Percent { get; set; }
    }

    public class LootboxEnchantOddsDto
    {
        [JsonPropertyName("enchantRollId")]
        public int EnchantRollId { get; set; }

        [JsonPropertyName("enchantmentDefinitionId")]
        public int EnchantmentDefinitionId { get; set; }

        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("isCustom")]
        public bool IsCustom { get; set; }

        // Per enchantable item.
        [JsonPropertyName("hitPercent")]
        public double HitPercent { get; set; }

        [JsonPropertyName("minLevel")]
        public int MinLevel { get; set; }

        [JsonPropertyName("maxLevel")]
        public int MaxLevel { get; set; }

        // The levels it can actually give on each item grade after the cap; null min/max = always dropped.
        [JsonPropertyName("levelsByGrade")]
        public List<LootboxEnchantLevelRangeDto> LevelsByGrade { get; set; } = new();
    }

    public class LootboxEnchantLevelRangeDto
    {
        [JsonPropertyName("gradeId")]
        public int GradeId { get; set; }

        [JsonPropertyName("stars")]
        public int Stars { get; set; }

        [JsonPropertyName("minLevel")]
        public int? MinLevel { get; set; }

        [JsonPropertyName("maxLevel")]
        public int? MaxLevel { get; set; }
    }
}
