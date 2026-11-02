using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace knkwebapi_v2.Dtos
{
    // Read DTO - Full representation
    public class EnchantmentDefinitionReadDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("isCustom")]
        public bool IsCustom { get; set; }

        [JsonPropertyName("maxLevel")]
        public int MaxLevel { get; set; } = 1;

        [JsonPropertyName("minecraftEnchantmentRefId")]
        public int? MinecraftEnchantmentRefId { get; set; }

        [JsonPropertyName("baseEnchantmentRef")]
        public MinecraftEnchantmentRefNavDto? BaseEnchantmentRef { get; set; }

        [JsonPropertyName("defaultForBlueprints")]
        public List<ItemBlueprintNavDto> DefaultForBlueprints { get; set; } = new();

        [JsonPropertyName("abilityDefinition")]
        public AbilityDefinitionReadDto? AbilityDefinition { get; set; }
        // PandiO/knk-workspace#151: combat values of a custom enchantment; null = the plugin's built-in value.
        [JsonPropertyName("procChancePercent")]
        public double? ProcChancePercent { get; set; }

        [JsonPropertyName("procChancePerLevelPercent")]
        public double? ProcChancePerLevelPercent { get; set; }

        [JsonPropertyName("cooldownEnabled")]
        public bool? CooldownEnabled { get; set; }

        [JsonPropertyName("cooldownSeconds")]
        public int? CooldownSeconds { get; set; }

        [JsonPropertyName("effectDurationSeconds")]
        public double? EffectDurationSeconds { get; set; }

        [JsonPropertyName("levelMultiplier")]
        public double? LevelMultiplier { get; set; }
    }

    // Create DTO
    public class EnchantmentDefinitionCreateDto
    {
        [Required]
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [Required]
        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("isCustom")]
        public bool IsCustom { get; set; }

        [JsonPropertyName("maxLevel")]
        public int MaxLevel { get; set; } = 1;

        [JsonPropertyName("minecraftEnchantmentRefId")]
        public int? MinecraftEnchantmentRefId { get; set; }

        // Optional: for hybrid picker - namespace key if enchantment not persisted yet
        [JsonPropertyName("enchantmentNamespaceKey")]
        public string? EnchantmentNamespaceKey { get; set; }

        // KNG-195: the web app's hybrid enchantment picker (and an Object picker on MinecraftEnchantmentRef)
        // names the key after the form field (MinecraftEnchantmentRefId -> MinecraftEnchantmentRefNamespaceKey).
        // Write-only alias of enchantmentNamespaceKey, so a catalog enchantment that isn't saved yet (sent
        // with id 0) is linked instead of dropped.
        [JsonPropertyName("minecraftEnchantmentRefNamespaceKey")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MinecraftEnchantmentRefNamespaceKey
        {
            get => null;
            set { if (!string.IsNullOrWhiteSpace(value)) EnchantmentNamespaceKey = value; }
        }

        [JsonPropertyName("abilityDefinition")]
        public AbilityDefinitionUpsertDto? AbilityDefinition { get; set; }
        // PandiO/knk-workspace#151: combat values of a custom enchantment; null = the plugin's built-in value.
        [JsonPropertyName("procChancePercent")]
        public double? ProcChancePercent { get; set; }

        [JsonPropertyName("procChancePerLevelPercent")]
        public double? ProcChancePerLevelPercent { get; set; }

        [JsonPropertyName("cooldownEnabled")]
        public bool? CooldownEnabled { get; set; }

        [JsonPropertyName("cooldownSeconds")]
        public int? CooldownSeconds { get; set; }

        [JsonPropertyName("effectDurationSeconds")]
        public double? EffectDurationSeconds { get; set; }

        [JsonPropertyName("levelMultiplier")]
        public double? LevelMultiplier { get; set; }
    }

    // Update DTO
    public class EnchantmentDefinitionUpdateDto
    {
        [Required]
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [Required]
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [Required]
        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("isCustom")]
        public bool IsCustom { get; set; }

        [JsonPropertyName("maxLevel")]
        public int MaxLevel { get; set; } = 1;

        [JsonPropertyName("minecraftEnchantmentRefId")]
        public int? MinecraftEnchantmentRefId { get; set; }

        [JsonPropertyName("enchantmentNamespaceKey")]
        public string? EnchantmentNamespaceKey { get; set; }

        // KNG-195: the web app's hybrid enchantment picker (and an Object picker on MinecraftEnchantmentRef)
        // names the key after the form field (MinecraftEnchantmentRefId -> MinecraftEnchantmentRefNamespaceKey).
        // Write-only alias of enchantmentNamespaceKey, so a catalog enchantment that isn't saved yet (sent
        // with id 0) is linked instead of dropped.
        [JsonPropertyName("minecraftEnchantmentRefNamespaceKey")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MinecraftEnchantmentRefNamespaceKey
        {
            get => null;
            set { if (!string.IsNullOrWhiteSpace(value)) EnchantmentNamespaceKey = value; }
        }

        // KNG-195: the web form never sends abilityDefinition, and treating "absent" as null deleted a
        // custom enchantment's ability on every save. The service now only writes it when the request
        // contained it (explicit null still removes it): System.Text.Json calls a setter only for
        // properties present in the JSON. Same pattern as GradeUpdateDto.
        private AbilityDefinitionUpsertDto? _abilityDefinition;

        [JsonPropertyName("abilityDefinition")]
        public AbilityDefinitionUpsertDto? AbilityDefinition
        {
            get => _abilityDefinition;
            set { _abilityDefinition = value; HasAbilityDefinition = true; }
        }

        [JsonIgnore]
        public bool HasAbilityDefinition { get; private set; }
        // PandiO/knk-workspace#151: combat values. Like abilityDefinition above, a value is only written when the
        // request contained it (explicit null resets it to the plugin's built-in value), so a form configuration
        // without these fields doesn't wipe them on every save.
        private double? _procChancePercent;
        private double? _procChancePerLevelPercent;
        private bool? _cooldownEnabled;
        private int? _cooldownSeconds;
        private double? _effectDurationSeconds;
        private double? _levelMultiplier;

        [JsonPropertyName("procChancePercent")]
        public double? ProcChancePercent
        {
            get => _procChancePercent;
            set { _procChancePercent = value; HasProcChancePercent = true; }
        }

        [JsonPropertyName("procChancePerLevelPercent")]
        public double? ProcChancePerLevelPercent
        {
            get => _procChancePerLevelPercent;
            set { _procChancePerLevelPercent = value; HasProcChancePerLevelPercent = true; }
        }

        [JsonPropertyName("cooldownEnabled")]
        public bool? CooldownEnabled
        {
            get => _cooldownEnabled;
            set { _cooldownEnabled = value; HasCooldownEnabled = true; }
        }

        [JsonPropertyName("cooldownSeconds")]
        public int? CooldownSeconds
        {
            get => _cooldownSeconds;
            set { _cooldownSeconds = value; HasCooldownSeconds = true; }
        }

        [JsonPropertyName("effectDurationSeconds")]
        public double? EffectDurationSeconds
        {
            get => _effectDurationSeconds;
            set { _effectDurationSeconds = value; HasEffectDurationSeconds = true; }
        }

        [JsonPropertyName("levelMultiplier")]
        public double? LevelMultiplier
        {
            get => _levelMultiplier;
            set { _levelMultiplier = value; HasLevelMultiplier = true; }
        }

        [JsonIgnore] public bool HasProcChancePercent { get; private set; }
        [JsonIgnore] public bool HasProcChancePerLevelPercent { get; private set; }
        [JsonIgnore] public bool HasCooldownEnabled { get; private set; }
        [JsonIgnore] public bool HasCooldownSeconds { get; private set; }
        [JsonIgnore] public bool HasEffectDurationSeconds { get; private set; }
        [JsonIgnore] public bool HasLevelMultiplier { get; private set; }
    }

    // List DTO - For search/list views
    public class EnchantmentDefinitionListDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("maxLevel")]
        public int MaxLevel { get; set; } = 1;

        [JsonPropertyName("isCustom")]
        public bool IsCustom { get; set; }

        [JsonPropertyName("minecraftEnchantmentRefId")]
        public int? MinecraftEnchantmentRefId { get; set; }

        [JsonPropertyName("baseEnchantmentNamespaceKey")]
        public string? BaseEnchantmentNamespaceKey { get; set; }

        [JsonPropertyName("blueprintCount")]
        public int BlueprintCount { get; set; }
    }

    // MinecraftEnchantmentRefNavDto for relationship
    public class MinecraftEnchantmentRefNavDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("namespaceKey")]
        public string NamespaceKey { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("maxLevel")]
        public int? MaxLevel { get; set; }

        [JsonPropertyName("iconUrl")]
        public string? IconUrl { get; set; }
    }

    public class AbilityDefinitionReadDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("enchantmentDefinitionId")]
        public int EnchantmentDefinitionId { get; set; }

        [JsonPropertyName("abilityKey")]
        public string AbilityKey { get; set; } = string.Empty;

        [JsonPropertyName("runtimeConfigJson")]
        public string? RuntimeConfigJson { get; set; }

        [JsonPropertyName("futureUserAssignmentContract")]
        public string? FutureUserAssignmentContract { get; set; }
    }

    public class AbilityDefinitionUpsertDto
    {
        [Required]
        [JsonPropertyName("abilityKey")]
        public string AbilityKey { get; set; } = string.Empty;

        [JsonPropertyName("runtimeConfigJson")]
        public string? RuntimeConfigJson { get; set; }

        [JsonPropertyName("futureUserAssignmentContract")]
        public string? FutureUserAssignmentContract { get; set; }
    }
}
