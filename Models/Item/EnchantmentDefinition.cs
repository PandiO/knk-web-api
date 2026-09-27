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
