namespace knkwebapi_v2.Enums;

// Siege Survival phase 4 (knk-workspace docs/specs/siege-survival/DESIGN.md §8a, S24, S39, S59;
// PandiO/knk-workspace#41, link S7a): the match-scoped Mystery Box roll rows. Stored as strings
// (HasConversion<string>()) and serialized by name by the global JsonStringEnumConverter.

/// <summary>The life of one roll: rolled (shown on the box), claimed by the roller, or forfeited (S59).</summary>
public enum SurvivalMysteryBoxRollState
{
    Rolled,
    Claimed,
    Forfeited
}

/// <summary>How the roller received the item (§8a "Claim"): into the inventory, or dropped as a match-scoped item when it was full.</summary>
public enum SurvivalMysteryBoxDeliveryKind
{
    Inventory,
    Drop
}

/// <summary>Why a roll was forfeited (S59): the claim timeout ran out, the roller died, left, or the match ended.</summary>
public enum SurvivalMysteryBoxForfeitReason
{
    Timeout,
    RollerDied,
    RollerLeft,
    MatchEnded
}
