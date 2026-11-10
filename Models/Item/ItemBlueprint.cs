using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

[FormConfigurableEntity("ItemBlueprint")]
public class ItemBlueprint
{
    public int Id { get; set; }
    public string? Name { get; set; } = string.Empty;
    public string? Description { get; set; } = string.Empty;
    [RelatedEntityField(typeof(MinecraftMaterialRef))]
    [NavigationPair("IconMaterial")]
    public int? IconMaterialRefId { get; set; }
    [RelatedEntityField(typeof(MinecraftMaterialRef))]
    public MinecraftMaterialRef? IconMaterial { get; set; } = null;

    public string DefaultDisplayName { get; set; } = string.Empty;
    public string? DefaultDisplayDescription { get; set; } = string.Empty;

    public int DefaultQuantity { get; set; } = 1;
    public int MaxStackSize { get; set; } = 64;

    [RelatedEntityField(typeof(Category))]
    [NavigationPair("Category")]
    public int? CategoryId { get; set; }
    [RelatedEntityField(typeof(Category))]
    public Category? Category { get; set; } = null;

    [RelatedEntityField(typeof(Grade))]
    [NavigationPair("Grade")]
    public int? GradeId { get; set; }
    [RelatedEntityField(typeof(Grade))]
    public Grade? Grade { get; set; } = null;

    public decimal BasePriceMin { get; set; }
    public decimal BasePriceMax { get; set; }

    [NavigationPair(nameof(ItemBlueprintDefaultEnchantment))]
    [RelatedEntityField(typeof(ItemBlueprintDefaultEnchantment))]
    public ICollection<int> DefaultEnchantmentIds { get; set; } = new List<int>();
    [RelatedEntityField(typeof(ItemBlueprintDefaultEnchantment))]
    public ICollection<ItemBlueprintDefaultEnchantment> DefaultEnchantments { get; set; } = new List<ItemBlueprintDefaultEnchantment>();

    [RelatedEntityField(typeof(ItemBlueprintTag))]
    public ICollection<ItemBlueprintTag> Tags { get; set; } = new List<ItemBlueprintTag>();

    [RelatedEntityField(typeof(ItemBlueprintOrigin))]
    public ICollection<ItemBlueprintOrigin> Origins { get; set; } = new List<ItemBlueprintOrigin>();

    // Shield banner (PandiO/knk-workspace#232): the shield's base colour plus its pattern layers, bottom to top, the
    // same data Minecraft keeps on the item (base colour + layers of pattern type and dye colour). Null/empty = a plain
    // shield. Only meaningful on a shield or banner material; knk-plugin applies it in ItemAppearanceBukkitMapper.
    public BannerDyeColor? ShieldBannerBaseColor { get; set; }
    public ICollection<ItemBlueprintBannerLayer> ShieldBannerLayers { get; set; } = new List<ItemBlueprintBannerLayer>();

    // Armor trim (#232): trim pattern and material registry keys ("minecraft:silence", "minecraft:netherite"), both
    // set or both null (ArmorTrimKeys). Only meaningful on an armour material.
    public string? ArmorTrimPattern { get; set; }
    public string? ArmorTrimMaterial { get; set; }
}