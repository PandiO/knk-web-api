using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// One arena duel (arena duels DESIGN.md §4.3, KNG-213): created at lock-in with both coin stakes escrowed in
/// SYS_DUEL, then settled exactly once. Append-only history: rows are never deleted, and only the lifecycle
/// and review columns change. Items are recorded for audit only; the plugin's escrow spool holds them
/// (DESIGN §7.2). Written by DuelMatchService only.
/// </summary>
public class DuelMatch
{
    public int Id { get; set; }

    /// <summary>The plugin's match token (a UUID), used in spool file names, logs and idempotency keys. Unique.</summary>
    public string PublicToken { get; set; } = null!;

    public int ArenaId { get; set; }
    public Arena? Arena { get; set; }

    /// <summary>Null until the fight starts on one.</summary>
    public int? BattlegroundId { get; set; }
    public ArenaBattleground? Battleground { get; set; }

    /// <summary>The inviter.</summary>
    public int ChallengerUserId { get; set; }
    public User? ChallengerUser { get; set; }

    public int OpponentUserId { get; set; }
    public User? OpponentUser { get; set; }

    public DuelGearMode GearMode { get; set; } = DuelGearMode.OWN_GEAR_KEEP;
    public int? FixedKitId { get; set; }
    public int? ChallengerKitId { get; set; }
    public int? OpponentKitId { get; set; }

    public DuelMatchStatus Status { get; set; } = DuelMatchStatus.Created;
    public DuelEndReason? EndReason { get; set; }
    public int? WinnerUserId { get; set; }

    /// <summary>Coins each side staked (≥ 0).</summary>
    public long ChallengerCoins { get; set; }
    public long OpponentCoins { get; set; }

    /// <summary>The arena's FeePercent at lock-in: the fee the duelists agreed to (D15).</summary>
    public int FeePercent { get; set; }

    /// <summary>The fee taken from the pot of a paid decisive result (0 otherwise).</summary>
    public long FeeCoins { get; set; }

    /// <summary>Audit summary of the staked items: [{ ownerUserId, material, amount, name, grade, instanceId }].</summary>
    public string? StakeItemsJson { get; set; }

    /// <summary>Audit summary of the drop-mode spoils (the loser's inventory), same shape.</summary>
    public string? SpoilsItemsJson { get; set; }

    /// <summary>Number of stake item stacks per side, from <see cref="StakeItemsJson"/> (kept for the limit
    /// and the value-moving test).</summary>
    public int ChallengerItemStacks { get; set; }
    public int OpponentItemStacks { get; set; }

    /// <summary>The DUEL_STAKE posting (null for a friendly duel or one staked with items only).</summary>
    public long? StakeTransactionId { get; set; }

    /// <summary>The DUEL_PAYOUT posting of a paid result.</summary>
    public long? SettlementTransactionId { get; set; }

    /// <summary>The DUEL_FEE move (SYS_DUEL → SYS_FEES) of a paid result.</summary>
    public long? FeeTransactionId { get; set; }

    /// <summary>The reversal of the stake (cancel, draw, abort, void).</summary>
    public long? RefundTransactionId { get; set; }

    // ===== Review (DESIGN §10.3) =====
    public DuelReviewState Review { get; set; } = DuelReviewState.None;

    /// <summary>0 clean, 1 flagged, 2 held; null when the result wasn't scored (friendly, draw, cancel).</summary>
    public int? ReviewTier { get; set; }

    /// <summary>The signals that fired, comma-separated ("S1,S4"); null for none.</summary>
    public string? ReviewSignals { get; set; }

    /// <summary>The numbers behind the signals and the escalation counts, for the staff page.</summary>
    public string? ReviewDetailsJson { get; set; }

    /// <summary>The DUEL_FLAG / DUEL_HOLD currency alert.</summary>
    public long? AlertId { get; set; }

    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }

    /// <summary>The staff member's reason for a void, revert or confiscation.</summary>
    public string? ReviewReason { get; set; }

    /// <summary>What a revert or confiscation recovered and what is still missing.</summary>
    public string? RevertResultJson { get; set; }

    /// <summary>What the plugin does with stakes and spoils it hasn't delivered yet.</summary>
    public DuelUndeliveredItems UndeliveredItems { get; set; } = DuelUndeliveredItems.None;

    /// <summary>Duration, damage and hits per side, the loser's attack attempts and (own gear) armour and
    /// main hand at fight start (DESIGN §10.3).</summary>
    public string? FightTelemetryJson { get; set; }

    public int OfferCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LockedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FightStartedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    public long Pot => ChallengerCoins + OpponentCoins;

    /// <summary>Coin or item stakes, or drop mode (DESIGN §10.3): the duels eligibility, the limits and
    /// the review apply to. Everything else is friendly.</summary>
    public bool IsValueMoving =>
        ChallengerCoins > 0 || OpponentCoins > 0 || ChallengerItemStacks > 0 || OpponentItemStacks > 0
        || GearMode == DuelGearMode.OWN_GEAR_DROP;

    public int? LoserUserId => WinnerUserId == null ? null
        : WinnerUserId == ChallengerUserId ? OpponentUserId : ChallengerUserId;

    public bool Involves(int userId) => ChallengerUserId == userId || OpponentUserId == userId;

    public long CoinsOf(int userId) => userId == ChallengerUserId ? ChallengerCoins : userId == OpponentUserId ? OpponentCoins : 0;
}
