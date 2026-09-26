using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// A lootbox as an inventory item (IMPLEMENTATION_PLAN.md Phase 5, DESIGN.md Q5): v1's "Sword Box" consumables,
/// server-issued. The plugin puts <see cref="Token"/> in the item's PDC (<c>knightsandkings:knk_lootbox_token</c>) and
/// identifies the item only by it, never by its name, which closes v1's anvil-rename exploit. Opening it redeems the
/// token: the same roll, daily cap and ItemInstance mint as a world box, with the claim pointing back here
/// (<see cref="LootboxClaim.LootboxTokenId"/>, unique). <see cref="Status"/> is a concurrency token, so a duplicated
/// item stack can be opened once; every other copy is refused. Not form-configurable.
/// </summary>
public class LootboxToken
{
    public int Id { get; set; }

    // The item's identity (unique, random): unguessable, never derived from anything the player controls.
    public Guid Token { get; set; } = Guid.NewGuid();

    public int LootboxTypeId { get; set; }
    public LootboxType LootboxType { get; set; } = null!;

    // Fixed at issue (a "Legendary Weapons Lootbox" stays legendary).
    public int BoxGradeId { get; set; }
    public Grade BoxGrade { get; set; } = null!;

    // The player it was issued to (who the plugin delivers it to); anyone holding the item may open it.
    public int? IssuedToUserId { get; set; }
    public User? IssuedToUser { get; set; }

    public LootboxTokenReason IssuedReason { get; set; } = LootboxTokenReason.Admin;

    // Issue idempotency: one issue request = one key, its tokens numbered 0..n-1; unique together.
    public string IssueKey { get; set; } = null!;
    public int IssueIndex { get; set; }

    public int? IssuedByUserId { get; set; }
    public User? IssuedByUser { get; set; }

    public string? Note { get; set; }

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    // The plugin put the item in the player's inventory (or dropped it owner-locked at their feet).
    public DateTime? DeliveredAt { get; set; }

    [ConcurrencyCheck]
    public LootboxTokenStatus Status { get; set; } = LootboxTokenStatus.Issued;

    public DateTime? RedeemedAt { get; set; }
    public int? RedeemedByUserId { get; set; }
    public User? RedeemedByUser { get; set; }

    public DateTime? RevokedAt { get; set; }

    // The claim this token was redeemed into (the FK lives on the claim, unique).
    public LootboxClaim? Claim { get; set; }
}
