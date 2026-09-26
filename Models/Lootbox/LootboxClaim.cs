using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// Append-only drop log (DESIGN.md §3.2), like <see cref="KitClaim"/>: one row per opened box or admin give. The
/// roll is persisted here before any item exists, so a relog, crash or retry replays it instead of re-rolling.
/// The rolled enchantments live on the minted <see cref="ItemInstance"/> (null for stackable items). Written from
/// Phase 2 on; not form-configurable.
/// </summary>
public class LootboxClaim
{
    public int Id { get; set; }

    // Unique; null for admin gives and (Phase 5) token items.
    public int? LootboxSpawnId { get; set; }
    public LootboxSpawn? LootboxSpawn { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int LootboxTypeId { get; set; }
    public LootboxType LootboxType { get; set; } = null!;

    public int BoxGradeId { get; set; }
    public Grade BoxGrade { get; set; } = null!;

    public int ItemBlueprintId { get; set; }
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    public int? ItemGradeId { get; set; }
    public Grade? ItemGrade { get; set; }

    public int Quantity { get; set; } = 1;
    public bool IsSpecial { get; set; }

    // Unique; null for stackable items.
    public long? ItemInstanceId { get; set; }
    public ItemInstance? ItemInstance { get; set; }

    // Unique, e.g. "{token}:{userId}"; the same key replays the stored result.
    public string IdempotencyKey { get; set; } = null!;

    public DateTime ClaimedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveredAt { get; set; }
    public LootboxDeliveryMethod? DeliveryMethod { get; set; }
    public string? DeliveryNote { get; set; }
}
