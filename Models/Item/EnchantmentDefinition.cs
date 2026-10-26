using knkwebapi_v2.Attributes;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Models;

[FormConfigurableEntity("EnchantmentDefinition")]
public class EnchantmentDefinition
{
    public int Id { get; set; }

    // "minecraft:sharpness" or "knk:lifesteal"
    public string Key { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public bool IsCustom { get; set; } = false;
    public int MaxLevel { get; set; } = 1;

    // PandiO/knk-workspace#151: admin-set combat values of a custom enchantment, read by the plugin's effect
    // handlers. Null = the plugin's built-in value for that enchantment; for the canonical custom enchantments the
    // startup seed writes the defaults into null columns (#156, AbilityDefinition.FillCombatDefaults). Vanilla
    // enchantments ignore them.
    // At level n: chance = ProcChancePercent + ProcChancePerLevelPercent x (n - 1), capped to 0-100;
    // duration = EffectDurationSeconds x (1 + LevelMultiplier x (n - 1)).

    /// <summary>On-hit chance at level I, in percent (0-100). Only on-hit enchantments roll it.</summary>
    public double? ProcChancePercent { get; set; }

    /// <summary>Percentage points added to the chance for every level above I (may be negative).</summary>
    public double? ProcChancePerLevelPercent { get; set; }

    /// <summary>Whether a proc or activation starts a cooldown for the player who used the enchantment.</summary>
    public bool? CooldownEnabled { get; set; }

    /// <summary>Cooldown length in seconds, per player who used the enchantment.</summary>
    public int? CooldownSeconds { get; set; }

    /// <summary>Effect duration at level I, in seconds.</summary>
    public double? EffectDurationSeconds { get; set; }

    /// <summary>Share of the level-I duration added per level above I (1 = level x the level-I duration, 0 = the same at every level).</summary>
    public double? LevelMultiplier { get; set; }

    // Many-to-One: EnchantmentDefinition → MinecraftEnchantmentRef (base enchantment reference)
    [NavigationPair(nameof(BaseEnchantmentRef))]
    [RelatedEntityField(typeof(MinecraftEnchantmentRef))]
    public int? MinecraftEnchantmentRefId { get; set; }
    
    [RelatedEntityField(typeof(MinecraftEnchantmentRef))]
    public MinecraftEnchantmentRef? BaseEnchantmentRef { get; set; }

    // Optional 1:1 extension for custom-only ability metadata
    [NavigationPair(nameof(AbilityDefinition))]
    [RelatedEntityField(typeof(AbilityDefinition))]
    public AbilityDefinition? AbilityDefinition { get; set; }

    // Many-to-Many: EnchantmentDefinition ↔ ItemBlueprint (via ItemBlueprintDefaultEnchantment)
    [NavigationPair(nameof(ItemBlueprintDefaultEnchantment))]
    [RelatedEntityField(typeof(ItemBlueprintDefaultEnchantment))]
    public ICollection<ItemBlueprintDefaultEnchantment> DefaultForBlueprints { get; set; } = new List<ItemBlueprintDefaultEnchantment>();
    
    // One-to-Many: EnchantmentDefinition → ItemInstanceEnchantment (the enchantment on minted items)
    public ICollection<ItemInstanceEnchantment> AppliedToInstances { get; set; } = new List<ItemInstanceEnchantment>();
}
