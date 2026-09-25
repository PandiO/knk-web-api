using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

// Kits — a reusable equipment/consumables loadout, grantable via self-claim, staff give, or a
// first-join welcome grant. See docs/specs/kits/DESIGN.md §2.1.
[FormConfigurableEntity("Kit")]
public class Kit
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    // Equipment loadout — all nullable (DESIGN.md §2.1: a Kit can be armor-only, weapon-only, or
    // consumables-only, and a partially-configured Kit can never NPE a grant). Restrict delete —
    // never Cascade — matches every other catalog-lookup FK in this codebase (ItemBlueprint's own
    // IconMaterialRefId/CategoryId/GradeId): an ItemBlueprint still referenced by a Kit slot
    // cannot be deleted out from under it.
    [RelatedEntityField(typeof(ItemBlueprint))]
    [NavigationPair("Helmet")]
    public int? HelmetId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint? Helmet { get; set; }

    [RelatedEntityField(typeof(ItemBlueprint))]
    [NavigationPair("Chestplate")]
    public int? ChestplateId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint? Chestplate { get; set; }

    [RelatedEntityField(typeof(ItemBlueprint))]
    [NavigationPair("Leggings")]
    public int? LeggingsId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint? Leggings { get; set; }

    [RelatedEntityField(typeof(ItemBlueprint))]
    [NavigationPair("Boots")]
    public int? BootsId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint? Boots { get; set; }

    [RelatedEntityField(typeof(ItemBlueprint))]
    [NavigationPair("Shield")]
    public int? ShieldId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint? Shield { get; set; }

    [RelatedEntityField(typeof(ItemBlueprint))]
    [NavigationPair("Hand")]
    public int? HandId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint? Hand { get; set; }

    // Bonus contents — slot-indexed, see KitContent for the cascade-delete fix (DESIGN.md §2.2).
    [RelatedEntityField(typeof(KitContent))]
    public ICollection<KitContent> Contents { get; set; } = new List<KitContent>();

    // Access gating — reuses the user-features/user-management permission model directly, no new
    // gating entity (DESIGN.md §0/§3). All three are optional and combine as AND.
    [RelatedEntityField(typeof(TitleBracket))]
    [NavigationPair("MinTitleBracket")]
    public int? MinTitleBracketId { get; set; }
    [RelatedEntityField(typeof(TitleBracket))]
    public TitleBracket? MinTitleBracket { get; set; }

    [RelatedEntityField(typeof(PermissionGroup))]
    [NavigationPair("RequiredPermissionGroup")]
    public int? RequiredPermissionGroupId { get; set; }
    [RelatedEntityField(typeof(PermissionGroup))]
    public PermissionGroup? RequiredPermissionGroup { get; set; }

    public string? RequiredPermissionNode { get; set; }

    // Availability
    public bool GrantOnFirstJoin { get; set; }
    public int CooldownSeconds { get; set; } // 0 = no cooldown (freely repeatable)

    // Per-claim cost — optional, charged every successful claim (not the one-time purchase below)
    public int? CostAmount { get; set; }
    public KitCostCurrency? CostCurrency { get; set; }

    // Single-purchase premium kit (DESIGN.md §5.2) — bought once (Gems), then claimable
    // indefinitely with no further cost or cooldown.
    public bool IsSinglePurchasePremium { get; set; }
    public int? PremiumPriceGems { get; set; }
}

public enum KitCostCurrency
{
    Coins,
    Gems
}
