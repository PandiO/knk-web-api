namespace knkwebapi_v2.Enums;

// Arena duels match record (DESIGN.md §4.3, KNG-213). All stored as strings; never rename a value once
// it has been written. DuelGearMode lives in its own file (KNG-210).

/// <summary>Where a duel match is in its life. Created = stakes escrowed (lock-in), InProgress = the fight
/// started, then exactly one of the three end states.</summary>
public enum DuelMatchStatus
{
    Created,
    InProgress,
    Completed,
    Cancelled,
    Aborted
}

/// <summary>
/// Why a match ended. Completed: Win and Forfeit (decisive), TimeLimit / AgreedDraw / SimultaneousDeath
/// (draws, full refund). Cancelled: TeleportFailed, PlayerLeftLobby, NoBattleground, AdminCancelled.
/// Aborted: ServerRestart, ApiOutage.
/// </summary>
public enum DuelEndReason
{
    Win,
    Forfeit,
    TimeLimit,
    AgreedDraw,
    SimultaneousDeath,
    TeleportFailed,
    PlayerLeftLobby,
    NoBattleground,
    AdminCancelled,
    ServerRestart,
    ApiOutage
}

/// <summary>
/// Staff review of a decisive value-moving result (DESIGN.md §10.3). None = clean (tier 0) or not scored;
/// Flagged = tier 1, paid with a DUEL_FLAG alert; Held = tier 2, not paid until staff act; Released = a hold
/// staff paid out; Voided = refunded (a held result voided, or a paid result reverted or confiscated).
/// </summary>
public enum DuelReviewState
{
    None,
    Flagged,
    Held,
    Released,
    Voided
}

/// <summary>What the plugin does with stake items and spoils it hasn't delivered yet (the spool's
/// CLAIM_PENDING and HELD stacks) after a staff action: nothing special, give each stack back to its
/// original owner (void, revert), or destroy it (confiscate).</summary>
public enum DuelUndeliveredItems
{
    None,
    ReturnToOwners,
    Destroy
}

/// <summary>The money side of a result as the plugin needs it: Paid = the winner got the pot (pay out the
/// items too); Held = under review, keep everything in escrow; Refunded = each side got its own stake back.</summary>
public enum DuelSettlement
{
    None,
    Paid,
    Held,
    Refunded
}
