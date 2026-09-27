using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos;

// ===== Staff currency tooling (currency-payments IMPLEMENTATION_PLAN.md Phase 4) =====

/// <summary>
/// Body of POST api/currency/admin/adjustments: one staff Add/Remove/Set of a player's coins,
/// gems or XP (currency DESIGN.md §3.4). The ledger reason code follows the mode (ADMIN_GRANT /
/// ADMIN_TAKE / ADMIN_SET); <see cref="Category"/> says why in one word and <see cref="Note"/>
/// in a sentence.
/// </summary>
public class AdminAdjustmentDto
{
    [JsonPropertyName("targetUserId")]
    public int TargetUserId { get; set; }

    /// <summary>"Coins", "Gems" or "Experience".</summary>
    [JsonPropertyName("currency")]
    public Enums.Currency Currency { get; set; }

    /// <summary>"Add", "Remove" or "Set" (Set takes the target balance).</summary>
    [JsonPropertyName("mode")]
    public Enums.CurrencyOperation Mode { get; set; }

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    /// <summary>The balance the staff member saw; a mismatch is refused with 409 (stale screen).</summary>
    [JsonPropertyName("expectedCurrent")]
    public long? ExpectedCurrent { get; set; }

    /// <summary>One of <see cref="AdminAdjustmentCategories.All"/>.</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = null!;

    /// <summary>Why, at least <see cref="AdminAdjustmentCategories.MinNoteLength"/> characters (≤ 450).</summary>
    [JsonPropertyName("note")]
    public string Note { get; set; } = null!;

    /// <summary>Queue a resulting title change for the plugin to show in-game.</summary>
    [JsonPropertyName("notifyPlayer")]
    public bool NotifyPlayer { get; set; } = true;
}

/// <summary>The reason categories a staff adjustment or reversal picks from (the web app's dropdown).</summary>
public static class AdminAdjustmentCategories
{
    /// <summary>A staff note must say more than "fix" or "test".</summary>
    public const int MinNoteLength = 10;

    /// <summary>Longest note: the stored reason is "[Category] note" in 500 characters.</summary>
    public const int MaxNoteLength = 450;

    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        ["COMPENSATION"] = "Compensation",
        ["EVENT_PRIZE"] = "Event prize",
        ["REFUND"] = "Refund",
        ["CORRECTION"] = "Correction",
        ["PENALTY"] = "Penalty",
        ["TESTING"] = "Testing",
        ["OTHER"] = "Other"
    };
}

/// <summary>Body of POST api/currency/admin/transactions/{publicId}/reverse (DESIGN.md D11).</summary>
public class ReverseTransactionDto
{
    /// <summary>Why, at least <see cref="AdminAdjustmentCategories.MinNoteLength"/> characters.</summary>
    [JsonPropertyName("note")]
    public string Note { get; set; } = null!;

    /// <summary>When a player has since spent some of it: reverse what is there and record the
    /// shortfall, instead of refusing (ReversalWouldGoNegative).</summary>
    [JsonPropertyName("allowPartial")]
    public bool AllowPartial { get; set; }
}

/// <summary>Result of a reversal: the new REVERSAL posting and the transaction it reversed.</summary>
public class ReversalResultDto
{
    [JsonPropertyName("reversedPublicId")]
    public string ReversedPublicId { get; set; } = null!;

    [JsonPropertyName("posting")]
    public PostingResult Posting { get; set; } = null!;

    /// <summary>Reversed less than the original because the balance was short (allowPartial).</summary>
    [JsonPropertyName("partial")]
    public bool Partial { get; set; }
}

/// <summary>One leg of a transaction in the staff detail view — user and system accounts.</summary>
public class CurrencyEntryDetailDto
{
    [JsonPropertyName("entryId")]
    public long EntryId { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = null!;

    /// <summary>"User" or "System".</summary>
    [JsonPropertyName("accountKind")]
    public string AccountKind { get; set; } = null!;

    [JsonPropertyName("userId")]
    public int? UserId { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("systemAccount")]
    public string? SystemAccount { get; set; }

    [JsonPropertyName("operation")]
    public string Operation { get; set; } = null!;

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("balanceBefore")]
    public long? BalanceBefore { get; set; }

    [JsonPropertyName("balanceAfter")]
    public long? BalanceAfter { get; set; }
}

/// <summary>GET api/currency/admin/transactions/{publicId}: the header, every leg, and its reversal links.</summary>
public class CurrencyTransactionDetailDto
{
    [JsonPropertyName("transactionId")]
    public long TransactionId { get; set; }

    [JsonPropertyName("publicId")]
    public string PublicId { get; set; } = null!;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = null!;

    [JsonPropertyName("reasonCode")]
    public string ReasonCode { get; set; } = null!;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = null!;

    [JsonPropertyName("sourceType")]
    public string? SourceType { get; set; }

    [JsonPropertyName("sourceRef")]
    public string? SourceRef { get; set; }

    [JsonPropertyName("initiator")]
    public string Initiator { get; set; } = null!;

    [JsonPropertyName("initiatorUserId")]
    public int? InitiatorUserId { get; set; }

    [JsonPropertyName("initiatorUsername")]
    public string? InitiatorUsername { get; set; }

    [JsonPropertyName("initiatorComponent")]
    public string? InitiatorComponent { get; set; }

    [JsonPropertyName("idempotencyScope")]
    public string IdempotencyScope { get; set; } = null!;

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }

    [JsonPropertyName("metadataJson")]
    public string? MetadataJson { get; set; }

    /// <summary>Set on a reversal: the transaction it reversed.</summary>
    [JsonPropertyName("reversesPublicId")]
    public string? ReversesPublicId { get; set; }

    /// <summary>Set when this transaction has been reversed.</summary>
    [JsonPropertyName("reversedByPublicId")]
    public string? ReversedByPublicId { get; set; }

    /// <summary>Not a reversal and not reversed yet.</summary>
    [JsonPropertyName("reversible")]
    public bool Reversible { get; set; }

    [JsonPropertyName("entries")]
    public List<CurrencyEntryDetailDto> Entries { get; set; } = new();
}

/// <summary>A player's transfer lock (PUT/DELETE api/currency/admin/users/{id}/transfer-lock).</summary>
public class TransferLockDto
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("locked")]
    public bool Locked { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("lockedAt")]
    public DateTime? LockedAt { get; set; }
}

/// <summary>Body of PUT api/currency/admin/users/{id}/transfer-lock.</summary>
public class SetTransferLockDto
{
    /// <summary>Why (required, ≤ 200 characters); shown to staff.</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = null!;
}

/// <summary>
/// A currency's policy row (DESIGN.md §3.5): the transfer rules, the admin grant cap and the
/// signup grant. The same shape is PUT back to edit it (Currency/UpdatedBy/HardMaxBalance ignored;
/// UpdatedAt must be the value loaded - a newer row answers 409 PolicyChanged).
/// 0 on a cap, cooldown or hourly limit means "no limit".
/// </summary>
public class CurrencyPolicyDto
{
    /// <summary>"Coins" or "Gems".</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = null!;

    /// <summary>Kill switch for player transfers.</summary>
    [JsonPropertyName("transfersEnabled")]
    public bool TransfersEnabled { get; set; }

    /// <summary>Players may send this currency at all (gems: off, DESIGN.md §5 Q2).</summary>
    [JsonPropertyName("transferable")]
    public bool Transferable { get; set; }

    [JsonPropertyName("minTransfer")]
    public long MinTransfer { get; set; }

    [JsonPropertyName("maxTransfer")]
    public long MaxTransfer { get; set; }

    [JsonPropertyName("dailySendCap")]
    public long DailySendCap { get; set; }

    [JsonPropertyName("dailyReceiveCap")]
    public long DailyReceiveCap { get; set; }

    [JsonPropertyName("confirmThreshold")]
    public long ConfirmThreshold { get; set; }

    [JsonPropertyName("confirmTtlSeconds")]
    public int ConfirmTtlSeconds { get; set; }

    [JsonPropertyName("cooldownSeconds")]
    public int CooldownSeconds { get; set; }

    [JsonPropertyName("maxTransfersPerHour")]
    public int MaxTransfersPerHour { get; set; }

    [JsonPropertyName("minSenderAccountAgeHours")]
    public int MinSenderAccountAgeHours { get; set; }

    /// <summary>Lowest title bracket a sender must hold; null = no title gate.</summary>
    [JsonPropertyName("minSenderTitleBracketId")]
    public int? MinSenderTitleBracketId { get; set; }

    /// <summary>Transfer fee in basis points (100 = 1 %).</summary>
    [JsonPropertyName("transferFeeBasisPoints")]
    public int TransferFeeBasisPoints { get; set; }

    [JsonPropertyName("maxBalance")]
    public long MaxBalance { get; set; }

    /// <summary>What one staff member may grant per 24 h without knk.admin.currency.unlimited.</summary>
    [JsonPropertyName("adminDailyGrantCapPerActor")]
    public long AdminDailyGrantCapPerActor { get; set; }

    [JsonPropertyName("signupGrant")]
    public long SignupGrant { get; set; }

    /// <summary>The row's version: send back exactly what was loaded.</summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("updatedByUserId")]
    public int? UpdatedByUserId { get; set; }

    /// <summary>The balance cap the database enforces (BalanceLimits); MaxBalance may only be lower.</summary>
    [JsonPropertyName("hardMaxBalance")]
    public long HardMaxBalance { get; set; }
}
