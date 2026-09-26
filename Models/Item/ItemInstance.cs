using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// One physical, non-stackable item in the game (vision §9.1, minimal version from knk-workspace
/// <c>docs/specs/lootboxes/DESIGN.md</c> §3.2 / D16). knk-plugin stamps the <see cref="Id"/> into the item's PDC
/// (<c>knightsandkings:knk_item_instance</c>), so the id - not the lore - is the item's identity.
/// <para>
/// Minted by services inside the caller's transaction (<c>IItemInstanceService.BuildAsync</c>), never through a
/// create/update endpoint, so it is deliberately NOT [FormConfigurableEntity]. Stackable items get no instance:
/// a per-item id would stop them from stacking.
/// </para>
/// <para>
/// Deferred (documented, not built): ownership transfer (<see cref="OwnerCount"/> stays 1), per-field override
/// flags other than <see cref="CustomDisplayName"/>, cascade-to-instances tooling, and setting/enforcing
/// <see cref="IsSoulbound"/>/<see cref="IsGhosted"/>.
/// </para>
/// </summary>
public class ItemInstance
{
    // long: vision §9.1 expects millions of rows.
    public long Id { get; set; }

    public int ItemBlueprintId { get; set; }
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    // Frozen at mint: a later change to the blueprint's grade doesn't touch existing instances.
    public int? GradeId { get; set; }
    public Grade? Grade { get; set; }

    public int? OwnerUserId { get; set; }
    public User? OwnerUser { get; set; }

    public ItemInstanceOrigin Origin { get; set; } = ItemInstanceOrigin.Unknown;

    // What minted it, e.g. the LootboxClaim id for Origin=Lootbox.
    public string? OriginRef { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int OwnerCount { get; set; } = 1;

    // Non-null = a player/admin override that a future blueprint-to-instance cascade must skip.
    public string? CustomDisplayName { get; set; }

    public bool IsSoulbound { get; set; }
    public bool IsGhosted { get; set; }

    public ICollection<ItemInstanceEnchantment> Enchantments { get; set; } = new List<ItemInstanceEnchantment>();
}
