using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Dtos
{
    // Arena duels match record, review and restrictions (DESIGN.md §4.3, §6.5, §7, §10; KNG-213).
    // Enums travel as strings (JsonStringEnumConverter). The endpoint contract is in the chain report's P2
    // section (knk-workspace docs/reports/2026-10-21-arena-duels-chain.md).

    /// <summary>One staked stack or spoils stack, as an audit summary (the plugin's spool holds the item itself).</summary>
    public class DuelItemDto
    {
        [JsonPropertyName("ownerUserId")] public int OwnerUserId { get; set; }
        [JsonPropertyName("material")] public string Material { get; set; } = "";
        [JsonPropertyName("amount")] public int Amount { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("grade")] public string? Grade { get; set; }
        [JsonPropertyName("instanceId")] public long? InstanceId { get; set; }
    }

    /// <summary>
    /// <c>POST api/DuelMatches</c> (lock-in, DESIGN §6.5 step 3) and <c>POST api/DuelMatches/check</c> (the
    /// same rules, nothing written). <see cref="PublicToken"/> is the plugin's match token (a UUID); the
    /// Idempotency-Key header, when sent, must be <c>duel-lock:{publicToken}</c>.
    /// </summary>
    public class DuelMatchCreateDto
    {
        [JsonPropertyName("publicToken")] public string PublicToken { get; set; } = "";
        [JsonPropertyName("arenaId")] public int ArenaId { get; set; }
        [JsonPropertyName("challengerUserId")] public int ChallengerUserId { get; set; }
        [JsonPropertyName("opponentUserId")] public int OpponentUserId { get; set; }
        [JsonPropertyName("gearMode")] public DuelGearMode GearMode { get; set; } = DuelGearMode.OWN_GEAR_KEEP;
        [JsonPropertyName("fixedKitId")] public int? FixedKitId { get; set; }
        [JsonPropertyName("challengerCoins")] public long ChallengerCoins { get; set; }
        [JsonPropertyName("opponentCoins")] public long OpponentCoins { get; set; }

        /// <summary>Gems can't be staked (D14); anything but 0 is refused with GemsNotStakeable.</summary>
        [JsonPropertyName("challengerGems")] public long ChallengerGems { get; set; }
        [JsonPropertyName("opponentGems")] public long OpponentGems { get; set; }

        /// <summary>Both sides' stake items; each names its owner.</summary>
        [JsonPropertyName("stakeItems")] public List<DuelItemDto>? StakeItems { get; set; }

        /// <summary>How many offers the negotiation took (1–6).</summary>
        [JsonPropertyName("offerCount")] public int OfferCount { get; set; } = 1;

        [JsonPropertyName("lockedAt")] public DateTime? LockedAt { get; set; }
    }

    /// <summary>One reason a duel isn't allowed, with the same code the create refusal carries.</summary>
    public class DuelRefusalDto
    {
        [JsonPropertyName("code")] public string Code { get; set; } = "";
        [JsonPropertyName("message")] public string Message { get; set; } = "";
        [JsonPropertyName("details")] public object? Details { get; set; }
    }

    /// <summary><c>POST api/DuelMatches/check</c>: whether a create with this body would pass right now.</summary>
    public class DuelCheckResultDto
    {
        [JsonPropertyName("allowed")] public bool Allowed { get; set; }
        [JsonPropertyName("valueMoving")] public bool ValueMoving { get; set; }

        /// <summary>The fee a decisive result would take from this pot.</summary>
        [JsonPropertyName("feeCoins")] public long FeeCoins { get; set; }
        [JsonPropertyName("feePercent")] public int FeePercent { get; set; }
        [JsonPropertyName("refusals")] public List<DuelRefusalDto> Refusals { get; set; } = new();
    }

    /// <summary><c>POST api/DuelMatches/{id}/start-fight</c>: the battleground and, for KIT_CHOICE, each side's kit.</summary>
    public class DuelStartFightDto
    {
        [JsonPropertyName("battlegroundId")] public int BattlegroundId { get; set; }
        [JsonPropertyName("challengerKitId")] public int? ChallengerKitId { get; set; }
        [JsonPropertyName("opponentKitId")] public int? OpponentKitId { get; set; }
        [JsonPropertyName("startedAt")] public DateTime? StartedAt { get; set; }
    }

    /// <summary>One side's fight counters (DESIGN §10.3). Gear fields are measured at fight start, own-gear modes only.</summary>
    public class DuelSideTelemetryDto
    {
        [JsonPropertyName("damageDealt")] public double DamageDealt { get; set; }
        [JsonPropertyName("damageTaken")] public double DamageTaken { get; set; }
        [JsonPropertyName("hits")] public int Hits { get; set; }
        [JsonPropertyName("attackAttempts")] public int AttackAttempts { get; set; }
        [JsonPropertyName("armorPoints")] public int? ArmorPoints { get; set; }
        [JsonPropertyName("mainHand")] public string? MainHand { get; set; }
        [JsonPropertyName("hadWeapon")] public bool? HadWeapon { get; set; }
    }

    public class DuelTelemetryDto
    {
        /// <summary>FIGHT start to the end; 0 for a forfeit before the fight.</summary>
        [JsonPropertyName("durationSeconds")] public double DurationSeconds { get; set; }
        [JsonPropertyName("challenger")] public DuelSideTelemetryDto? Challenger { get; set; }
        [JsonPropertyName("opponent")] public DuelSideTelemetryDto? Opponent { get; set; }
    }

    /// <summary><c>POST api/DuelMatches/{id}/complete</c>: a decisive result (Win or Forfeit). Draws go through cancel.</summary>
    public class DuelMatchCompleteDto
    {
        [JsonPropertyName("winnerUserId")] public int WinnerUserId { get; set; }
        [JsonPropertyName("endReason")] public DuelEndReason EndReason { get; set; } = DuelEndReason.Win;
        [JsonPropertyName("telemetry")] public DuelTelemetryDto? Telemetry { get; set; }

        /// <summary>Drop mode: the loser's inventory the winner receives (audit summary).</summary>
        [JsonPropertyName("spoilsItems")] public List<DuelItemDto>? SpoilsItems { get; set; }
        [JsonPropertyName("endedAt")] public DateTime? EndedAt { get; set; }
    }

    /// <summary>
    /// <c>POST api/DuelMatches/{id}/cancel</c>: every end that is not a win (DESIGN §7.5). Cancel reasons set
    /// Cancelled, ServerRestart/ApiOutage set Aborted, TimeLimit/AgreedDraw/SimultaneousDeath set Completed
    /// with no winner. The stake is reversed once; no fee.
    /// </summary>
    public class DuelMatchCancelDto
    {
        [JsonPropertyName("endReason")] public DuelEndReason EndReason { get; set; } = DuelEndReason.AdminCancelled;
        [JsonPropertyName("telemetry")] public DuelTelemetryDto? Telemetry { get; set; }
        [JsonPropertyName("endedAt")] public DateTime? EndedAt { get; set; }
    }

    public class DuelAbortUnfinishedDto
    {
        [JsonPropertyName("endReason")] public DuelEndReason EndReason { get; set; } = DuelEndReason.ServerRestart;
    }

    public class DuelAbortUnfinishedResultDto
    {
        [JsonPropertyName("abortedMatchIds")] public List<int> AbortedMatchIds { get; set; } = new();
    }

    /// <summary>A staff release, void, revert or confiscation: the reason is required except for a release.</summary>
    public class DuelStaffActionDto
    {
        [JsonPropertyName("reason")] public string? Reason { get; set; }
    }

    /// <summary>The answer to complete, cancel, release, void, revert and confiscate.</summary>
    public class DuelResultDto
    {
        [JsonPropertyName("match")] public DuelMatchDto Match { get; set; } = null!;

        /// <summary>Paid (pay out the items), Held (keep everything in escrow), Refunded (each side gets its own back).</summary>
        [JsonPropertyName("settlement")] public DuelSettlement Settlement { get; set; }

        /// <summary>True when the call changed nothing (a retry): <see cref="Match"/> is the stored result.</summary>
        [JsonPropertyName("alreadyDone")] public bool AlreadyDone { get; set; }

        /// <summary>Revert / confiscate only.</summary>
        [JsonPropertyName("revert")] public DuelRevertResultDto? Revert { get; set; }
    }

    /// <summary>What a revert or confiscation of a paid result recovered (DESIGN §10.3).</summary>
    public class DuelRevertResultDto
    {
        [JsonPropertyName("confiscated")] public bool Confiscated { get; set; }

        /// <summary>The payout the winner received.</summary>
        [JsonPropertyName("payoutCoins")] public long PayoutCoins { get; set; }

        /// <summary>Taken back from the winner (≤ the payout; less when they have spent it).</summary>
        [JsonPropertyName("recoveredCoins")] public long RecoveredCoins { get; set; }

        /// <summary>The fee reversed back into the pot (revert only).</summary>
        [JsonPropertyName("feeReversedCoins")] public long FeeReversedCoins { get; set; }
        [JsonPropertyName("returnedToLoserCoins")] public long ReturnedToLoserCoins { get; set; }
        [JsonPropertyName("returnedToWinnerCoins")] public long ReturnedToWinnerCoins { get; set; }

        /// <summary>Sent to the SYS_FEES sink (confiscate only).</summary>
        [JsonPropertyName("confiscatedCoins")] public long ConfiscatedCoins { get; set; }

        /// <summary>Revert: what the stakes are short (pot − returned). Confiscate: the payout not recovered.</summary>
        [JsonPropertyName("missingCoins")] public long MissingCoins { get; set; }

        /// <summary>Delivered items can't be pulled back; staff use /inventory and /enderchest (KNG-13).</summary>
        [JsonPropertyName("undeliveredItems")] public DuelUndeliveredItems UndeliveredItems { get; set; }

        [JsonPropertyName("transactionIds")] public List<long> TransactionIds { get; set; } = new();
    }

    public class DuelMatchDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("publicToken")] public string PublicToken { get; set; } = "";
        [JsonPropertyName("arenaId")] public int ArenaId { get; set; }
        [JsonPropertyName("arenaKey")] public string? ArenaKey { get; set; }
        [JsonPropertyName("arenaName")] public string? ArenaName { get; set; }
        [JsonPropertyName("battlegroundId")] public int? BattlegroundId { get; set; }
        [JsonPropertyName("battlegroundKey")] public string? BattlegroundKey { get; set; }
        [JsonPropertyName("challengerUserId")] public int ChallengerUserId { get; set; }
        [JsonPropertyName("challengerUsername")] public string? ChallengerUsername { get; set; }
        [JsonPropertyName("opponentUserId")] public int OpponentUserId { get; set; }
        [JsonPropertyName("opponentUsername")] public string? OpponentUsername { get; set; }
        [JsonPropertyName("gearMode")] public DuelGearMode GearMode { get; set; }
        [JsonPropertyName("fixedKitId")] public int? FixedKitId { get; set; }
        [JsonPropertyName("challengerKitId")] public int? ChallengerKitId { get; set; }
        [JsonPropertyName("opponentKitId")] public int? OpponentKitId { get; set; }
        [JsonPropertyName("status")] public DuelMatchStatus Status { get; set; }
        [JsonPropertyName("endReason")] public DuelEndReason? EndReason { get; set; }
        [JsonPropertyName("winnerUserId")] public int? WinnerUserId { get; set; }
        [JsonPropertyName("challengerCoins")] public long ChallengerCoins { get; set; }
        [JsonPropertyName("opponentCoins")] public long OpponentCoins { get; set; }
        [JsonPropertyName("feePercent")] public int FeePercent { get; set; }
        [JsonPropertyName("feeCoins")] public long FeeCoins { get; set; }
        [JsonPropertyName("valueMoving")] public bool ValueMoving { get; set; }
        [JsonPropertyName("stakeItems")] public List<DuelItemDto> StakeItems { get; set; } = new();
        [JsonPropertyName("spoilsItems")] public List<DuelItemDto> SpoilsItems { get; set; } = new();
        [JsonPropertyName("stakeTransactionId")] public long? StakeTransactionId { get; set; }
        [JsonPropertyName("settlementTransactionId")] public long? SettlementTransactionId { get; set; }
        [JsonPropertyName("feeTransactionId")] public long? FeeTransactionId { get; set; }
        [JsonPropertyName("refundTransactionId")] public long? RefundTransactionId { get; set; }
        [JsonPropertyName("review")] public DuelReviewState Review { get; set; }
        [JsonPropertyName("reviewTier")] public int? ReviewTier { get; set; }
        [JsonPropertyName("reviewSignals")] public List<string> ReviewSignals { get; set; } = new();
        [JsonPropertyName("reviewDetails")] public JsonElement? ReviewDetails { get; set; }
        [JsonPropertyName("alertId")] public long? AlertId { get; set; }
        [JsonPropertyName("reviewedByUserId")] public int? ReviewedByUserId { get; set; }
        [JsonPropertyName("reviewedAt")] public DateTime? ReviewedAt { get; set; }
        [JsonPropertyName("reviewReason")] public string? ReviewReason { get; set; }
        [JsonPropertyName("revertResult")] public JsonElement? RevertResult { get; set; }
        [JsonPropertyName("undeliveredItems")] public DuelUndeliveredItems UndeliveredItems { get; set; }
        [JsonPropertyName("telemetry")] public DuelTelemetryDto? Telemetry { get; set; }
        [JsonPropertyName("offerCount")] public int OfferCount { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("lockedAt")] public DateTime LockedAt { get; set; }
        [JsonPropertyName("fightStartedAt")] public DateTime? FightStartedAt { get; set; }
        [JsonPropertyName("endedAt")] public DateTime? EndedAt { get; set; }
    }

    /// <summary>The cross-arena review rules (GET/PUT api/DuelPolicy). PUT sends back the loaded updatedAt.</summary>
    public class DuelPolicyDto
    {
        [JsonPropertyName("s1Enabled")] public bool S1Enabled { get; set; }
        [JsonPropertyName("s1LoserDamagePercent")] public int S1LoserDamagePercent { get; set; }
        [JsonPropertyName("s2Enabled")] public bool S2Enabled { get; set; }
        [JsonPropertyName("s2ShortFightSeconds")] public int S2ShortFightSeconds { get; set; }
        [JsonPropertyName("s3Enabled")] public bool S3Enabled { get; set; }
        [JsonPropertyName("s4Enabled")] public bool S4Enabled { get; set; }
        [JsonPropertyName("s4MaxLoserAttackAttempts")] public int S4MaxLoserAttackAttempts { get; set; }
        [JsonPropertyName("s5Enabled")] public bool S5Enabled { get; set; }
        [JsonPropertyName("s5PairDuels")] public int S5PairDuels { get; set; }
        [JsonPropertyName("s6Enabled")] public bool S6Enabled { get; set; }
        [JsonPropertyName("s6MinYoungOpponents")] public int S6MinYoungOpponents { get; set; }
        [JsonPropertyName("s6YoungAccountDays")] public int S6YoungAccountDays { get; set; }
        [JsonPropertyName("s6WindowDays")] public int S6WindowDays { get; set; }
        [JsonPropertyName("escalationFlagCount")] public int EscalationFlagCount { get; set; }
        [JsonPropertyName("escalationWindowDays")] public int EscalationWindowDays { get; set; }
        [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; set; }
        [JsonPropertyName("updatedByUserId")] public int? UpdatedByUserId { get; set; }
    }

    public class DuelRestrictionDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("username")] public string? Username { get; set; }
        [JsonPropertyName("otherUserId")] public int? OtherUserId { get; set; }
        [JsonPropertyName("otherUsername")] public string? OtherUsername { get; set; }
        [JsonPropertyName("reason")] public string Reason { get; set; } = "";
        [JsonPropertyName("expiresAt")] public DateTime? ExpiresAt { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("createdByUserId")] public int? CreatedByUserId { get; set; }
        [JsonPropertyName("liftedAt")] public DateTime? LiftedAt { get; set; }
        [JsonPropertyName("liftedByUserId")] public int? LiftedByUserId { get; set; }
        [JsonPropertyName("liftReason")] public string? LiftReason { get; set; }
        [JsonPropertyName("active")] public bool Active { get; set; }
    }

    /// <summary>A player (no <see cref="OtherUserId"/>) or a pair; stakes only; optional end date.</summary>
    public class DuelRestrictionCreateDto
    {
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("otherUserId")] public int? OtherUserId { get; set; }
        [JsonPropertyName("reason")] public string Reason { get; set; } = "";
        [JsonPropertyName("expiresAt")] public DateTime? ExpiresAt { get; set; }
    }

    /// <summary>Clears the review escalation of a pair (both ids) or a beneficiary (one id): their open
    /// DUEL_FLAG alerts are acknowledged, so those flags stop counting (DESIGN §10.3).</summary>
    public class DuelEscalationClearDto
    {
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("otherUserId")] public int? OtherUserId { get; set; }
        [JsonPropertyName("reason")] public string? Reason { get; set; }
    }

    public class DuelEscalationClearResultDto
    {
        [JsonPropertyName("acknowledgedAlertIds")] public List<long> AcknowledgedAlertIds { get; set; } = new();
    }
}
