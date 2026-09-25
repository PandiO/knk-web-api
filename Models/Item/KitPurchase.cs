namespace knkwebapi_v2.Models;

// One-time premium-kit unlock record (docs/specs/kits/DESIGN.md §2.4) - deliberately a separate
// table from KitClaim: a single-purchase premium kit (Kit.IsSinglePurchasePremium) is bought once
// (exactly one row per user here), then claimable indefinitely with no cooldown and no per-claim
// cost. Same append-only, viewed-not-edited convention as KitClaim - no [FormConfigurableEntity].
public class KitPurchase
{
    public int Id { get; set; }

    public int KitId { get; set; }
    public Kit Kit { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public DateTime PurchasedAt { get; set; } = DateTime.UtcNow;
    public int GemsPaid { get; set; }
}
