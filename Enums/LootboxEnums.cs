namespace knkwebapi_v2.Enums;

// Lootbox enums (knk-workspace docs/specs/lootboxes/DESIGN.md §3.2). All stored as their string names.

/// <summary>Lifecycle of a world box. <see cref="Active"/> is the only claimable state.</summary>
public enum LootboxSpawnStatus
{
    Active = 0,
    Claimed = 1,
    Expired = 2,
    Removed = 3
}

/// <summary>A <see cref="knkwebapi_v2.Models.LootboxPoolEntry"/> either adds a blueprint to a type's pool or removes one.</summary>
public enum LootboxPoolMode
{
    Include = 0,
    Exclude = 1
}

/// <summary>How the plugin handed a claimed item over (DESIGN.md §3.4).</summary>
public enum LootboxDeliveryMethod
{
    Inventory = 0,
    DroppedOwned = 1,
    Redelivered = 2
}

/// <summary>
/// Lifecycle of a lootbox token item (IMPLEMENTATION_PLAN.md Phase 5). <see cref="Issued"/> is the only redeemable
/// state; a redeem flips it to <see cref="Redeemed"/> in the same transaction that writes the claim.
/// </summary>
public enum LootboxTokenStatus
{
    Issued = 0,
    Redeemed = 1,
    Revoked = 2
}

/// <summary>Why a token was issued (Phase 5 issuance paths; PvpKill and Referral are hooks for later features).</summary>
public enum LootboxTokenReason
{
    Admin = 0,
    PremiumTier = 1,
    Kit = 2,
    PvpKill = 3,
    Referral = 4,
    Other = 5
}
