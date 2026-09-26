using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// One lootbox type per ItemBlueprint <see cref="Category"/> row (knk-workspace docs/specs/lootboxes/DESIGN.md
/// §3.2, D9) - the unique index on <see cref="CategoryId"/> enforces it. Seeded disabled for every category, so
/// nothing spawns until an admin enables one. The grade weights, pool entries and enchant rolls are edited
/// through this type's form, like <see cref="Kit.Contents"/>.
/// </summary>
[FormConfigurableEntity("LootboxType")]
public class LootboxType
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;

    [RelatedEntityField(typeof(Category))]
    [NavigationPair(nameof(Category))]
    public int CategoryId { get; set; }
    [RelatedEntityField(typeof(Category))]
    public Category Category { get; set; } = null!;

    // The pool also takes blueprints from the category's descendants.
    public bool IncludeSubcategories { get; set; } = true;

    public bool Enabled { get; set; } = false;

    // Relative weight of this type among the enabled types allowed in a spawn area.
    public int SpawnWeight { get; set; } = 10;

    // Box grades ★1-5 only (DESIGN.md Q3/D5): MaxBoxStars is validated <= 5.
    public int MinBoxStars { get; set; } = 1;
    public int MaxBoxStars { get; set; } = 5;

    // Item-grade window = [max(1, boxStars - ItemStarSpread), boxStars] (DESIGN.md §3.1 step 2).
    public int ItemStarSpread { get; set; } = 2;

    // Model shown in the world; null = the category's icon, then minecraft:chest.
    [RelatedEntityField(typeof(MinecraftMaterialRef))]
    [NavigationPair(nameof(DisplayMaterial))]
    public int? DisplayMaterialRefId { get; set; }
    [RelatedEntityField(typeof(MinecraftMaterialRef))]
    public MinecraftMaterialRef? DisplayMaterial { get; set; }

    // Extra per-type limit per player per UTC day on top of LootboxConfiguration's global one; null = none.
    public int? MaxClaimsPerPlayerPerDay { get; set; }

    // Rare-drop broadcast threshold for this type; null = LootboxConfiguration.AnnounceMinItemStars.
    public int? AnnounceMinItemStars { get; set; }

    [RelatedEntityField(typeof(LootboxTypeGradeWeight))]
    public ICollection<LootboxTypeGradeWeight> GradeWeights { get; set; } = new List<LootboxTypeGradeWeight>();

    [RelatedEntityField(typeof(LootboxPoolEntry))]
    public ICollection<LootboxPoolEntry> PoolEntries { get; set; } = new List<LootboxPoolEntry>();

    [RelatedEntityField(typeof(LootboxEnchantRoll))]
    public ICollection<LootboxEnchantRoll> EnchantRolls { get; set; } = new List<LootboxEnchantRoll>();
}
