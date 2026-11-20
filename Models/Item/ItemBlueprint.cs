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

    // Item identity (PandiO/knk-workspace#141, production DESIGN §8): the vanilla Material this blueprint stands for.
    // Unique: each obtainable material maps to exactly one blueprint, so a plain vanilla item resolves to it. Generated
    // "vanilla shadow" blueprints in the "Uncurated" category hold the match until an admin moves it to a curated
    // blueprint (api/ItemIdentity/matches). Not part of the create/update DTOs: only that endpoint and the shadow
    // generation set it, so a form that doesn't know the field can't clear it.
    [RelatedEntityField(typeof(MinecraftMaterialRef))]
    [NavigationPair("VanillaMaterialMatch")]
    public int? VanillaMaterialMatchRefId { get; set; }
    [RelatedEntityField(typeof(MinecraftMaterialRef))]
    public MinecraftMaterialRef? VanillaMaterialMatch { get; set; } = null;

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

    // Stock core P1 (PandiO/knk-workspace#97, production DESIGN § 4.4): capacity use = amount × StorageWeight, in raw
    // units. Default 1 for every item; per-item weights (bulky stone vs small gems) are later tuning, data only.
    public const decimal DefaultStorageWeight = 1m;
    public const decimal MinStorageWeight = 0.0001m;
    public const decimal MaxStorageWeight = 100_000m;
    public decimal StorageWeight { get; set; } = DefaultStorageWeight;

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

    // Potion type (siege shop, PandiO/knk-workspace#867, DESIGN § 9.4): the vanilla potion registry key
    // ("minecraft:strong_healing", "minecraft:regeneration"), null = none. Only meaningful on potion, splash potion,
    // lingering potion and tipped arrow materials; the material decides drink/splash. Keys in PotionTypeKeys.
    public string? PotionType { get; set; }
}