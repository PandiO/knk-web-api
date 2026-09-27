using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos;

/// <summary>A user's current balances, read from the users row (the ledger's materialized balance).</summary>
public class BalancesDto
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("coins")]
    public int Coins { get; set; }

    [JsonPropertyName("gems")]
    public int Gems { get; set; }

    [JsonPropertyName("experiencePoints")]
    public int ExperiencePoints { get; set; }
}

/// <summary>One user leg of a posting, as stored (identical on a replay).</summary>
public class PostedEntryDto
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    /// <summary>"Coins" | "Gems" | "Experience".</summary>
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = null!;

    /// <summary>"Add" | "Remove" | "Set".</summary>
    [JsonPropertyName("operation")]
    public string Operation { get; set; } = null!;

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("balanceBefore")]
    public long BalanceBefore { get; set; }

    [JsonPropertyName("balanceAfter")]
    public long BalanceAfter { get; set; }
}

/// <summary>
/// Result of a ledger posting (currency DESIGN.md §3.3). <see cref="Replayed"/> is true when the
/// idempotency key had already been posted with the same request: nothing new was written, and
/// <see cref="TransactionId"/>/<see cref="PublicId"/>/<see cref="Entries"/> are the original
/// posting's. <see cref="Balances"/> are the users' balances now (after this call), for caches.
/// </summary>
public class PostingResult
{
    [JsonPropertyName("transactionId")]
    public long TransactionId { get; set; }

    [JsonPropertyName("publicId")]
    public string PublicId { get; set; } = null!;

    [JsonPropertyName("replayed")]
    public bool Replayed { get; set; }

    [JsonPropertyName("reasonCode")]
    public string ReasonCode { get; set; } = null!;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("entries")]
    public List<PostedEntryDto> Entries { get; set; } = new();

    [JsonPropertyName("balances")]
    public Dictionary<int, BalancesDto> Balances { get; set; } = new();
}

/// <summary>One user leg with its transaction's context: a row of a player's balance history or
/// of the staff balance event log (KNG-23).</summary>
public class LedgerLineDto
{
    [JsonPropertyName("entryId")]
    public long EntryId { get; set; }

    [JsonPropertyName("transactionId")]
    public long TransactionId { get; set; }

    [JsonPropertyName("publicId")]
    public string PublicId { get; set; } = null!;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = null!;

    [JsonPropertyName("operation")]
    public string Operation { get; set; } = null!;

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("balanceBefore")]
    public long BalanceBefore { get; set; }

    [JsonPropertyName("balanceAfter")]
    public long BalanceAfter { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = null!;

    [JsonPropertyName("reasonCode")]
    public string ReasonCode { get; set; } = null!;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = null!;

    [JsonPropertyName("initiator")]
    public string Initiator { get; set; } = null!;

    [JsonPropertyName("initiatorUserId")]
    public int? InitiatorUserId { get; set; }

    [JsonPropertyName("initiatorUsername")]
    public string? InitiatorUsername { get; set; }

    [JsonPropertyName("initiatorComponent")]
    public string? InitiatorComponent { get; set; }

    [JsonPropertyName("sourceType")]
    public string? SourceType { get; set; }

    [JsonPropertyName("sourceRef")]
    public string? SourceRef { get; set; }

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }

    [JsonPropertyName("reversesTransactionId")]
    public long? ReversesTransactionId { get; set; }

    [JsonPropertyName("metadataJson")]
    public string? MetadataJson { get; set; }

    /// <summary>The other player of a player transfer (the recipient on the sender's line, the
    /// sender on the recipient's); null for every other kind.</summary>
    [JsonPropertyName("counterpartyUserId")]
    public int? CounterpartyUserId { get; set; }

    [JsonPropertyName("counterpartyUsername")]
    public string? CounterpartyUsername { get; set; }
}

/// <summary>A reconciliation finding (CurrencyReconciler): the users column and the ledger disagree.</summary>
public class CurrencyMismatchDto
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = null!;

    /// <summary>"BalanceColumn" (users column ≠ last BalanceAfter), "Arithmetic" (BalanceBefore + Amount ≠ BalanceAfter), "Chain" (a leg's
    /// BalanceBefore ≠ the previous leg's BalanceAfter) or "UnbalancedTransaction".</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = null!;

    [JsonPropertyName("expected")]
    public long? Expected { get; set; }

    [JsonPropertyName("actual")]
    public long? Actual { get; set; }

    [JsonPropertyName("entryId")]
    public long? EntryId { get; set; }

    [JsonPropertyName("transactionId")]
    public long? TransactionId { get; set; }
}

// ===== Player transfers (currency-payments IMPLEMENTATION_PLAN.md Phase 3) =====

/// <summary>Body of POST api/currency/transfers (in-game /pay; the game server is the only caller).</summary>
public class CreateTransferDto
{
    [JsonPropertyName("senderUserId")]
    public int SenderUserId { get; set; }

    [JsonPropertyName("recipientUserId")]
    public int RecipientUserId { get; set; }

    /// <summary>"Coins" | "Gems" (Experience is never transferable).</summary>
    [JsonPropertyName("currency")]
    public Enums.Currency Currency { get; set; }

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    /// <summary>Optional memo shown in both players' history (≤ 200 characters).</summary>
    [JsonPropertyName("note")]
    public string? Note { get; set; }

    /// <summary>The sender holds knk.pay.bypass in-game (staff paying out an event): skips the
    /// per-transfer maximum, daily caps, cooldown and account-age/title gate — never the balance,
    /// the recipient's cap, locks or the kill switch (DESIGN.md §3.5).</summary>
    [JsonPropertyName("bypassLimits")]
    public bool BypassLimits { get; set; }
}

/// <summary>A transfer waiting for the sender's confirmation (amount ≥ the policy's ConfirmThreshold).</summary>
public class PendingTransferDto
{
    [JsonPropertyName("publicId")]
    public string PublicId { get; set; } = null!;

    /// <summary>"Pending" | "Confirmed" | "Cancelled" | "Expired".</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = null!;

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = null!;

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("fee")]
    public long Fee { get; set; }

    [JsonPropertyName("recipientUserId")]
    public int RecipientUserId { get; set; }

    [JsonPropertyName("recipientUsername")]
    public string? RecipientUsername { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    /// <summary>Seconds left to confirm, from the server's clock (0 once expired).</summary>
    [JsonPropertyName("expiresInSeconds")]
    public int ExpiresInSeconds { get; set; }
}

/// <summary>
/// Result of POST api/currency/transfers and of a confirmation. <see cref="Status"/>
/// "Completed": the money moved (or, with <see cref="Replayed"/>, had already moved under this
/// key). "PendingConfirmation": nothing moved yet; confirm <see cref="Pending"/> within its
/// window. Only the sender's balance is returned — the sender never learns the recipient's.
/// </summary>
public class TransferResultDto
{
    public const string StatusCompleted = "Completed";
    public const string StatusPendingConfirmation = "PendingConfirmation";

    [JsonPropertyName("status")]
    public string Status { get; set; } = null!;

    [JsonPropertyName("transactionId")]
    public long? TransactionId { get; set; }

    [JsonPropertyName("publicId")]
    public string? PublicId { get; set; }

    [JsonPropertyName("replayed")]
    public bool Replayed { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = null!;

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("fee")]
    public long Fee { get; set; }

    [JsonPropertyName("senderUserId")]
    public int SenderUserId { get; set; }

    [JsonPropertyName("senderUsername")]
    public string? SenderUsername { get; set; }

    [JsonPropertyName("recipientUserId")]
    public int RecipientUserId { get; set; }

    [JsonPropertyName("recipientUsername")]
    public string? RecipientUsername { get; set; }

    /// <summary>The sender's current balances (for the plugin's cache and scoreboard).</summary>
    [JsonPropertyName("senderBalances")]
    public BalancesDto? SenderBalances { get; set; }

    [JsonPropertyName("pending")]
    public PendingTransferDto? Pending { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>Recipient's Minecraft UUID, for the PaymentReceived notification; not sent to clients.</summary>
    [JsonIgnore]
    public string? RecipientUuid { get; set; }

    /// <summary>Recipient's balance of the currency after the transfer, for their notification only.</summary>
    [JsonIgnore]
    public long RecipientBalanceAfter { get; set; }
}

/// <summary>What a player may send right now (GET api/currency/limits/{userId}), from the policy
/// and their own recent transfers. Informational — POST transfers re-checks everything.</summary>
public class TransferLimitsDto
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = null!;

    /// <summary>Transfers of this currency are switched on and it is transferable at all.</summary>
    [JsonPropertyName("transferable")]
    public bool Transferable { get; set; }

    [JsonPropertyName("minTransfer")]
    public long MinTransfer { get; set; }

    [JsonPropertyName("maxTransfer")]
    public long MaxTransfer { get; set; }

    [JsonPropertyName("dailySendCap")]
    public long DailySendCap { get; set; }

    [JsonPropertyName("sentLast24h")]
    public long SentLast24h { get; set; }

    [JsonPropertyName("remainingToday")]
    public long RemainingToday { get; set; }

    [JsonPropertyName("confirmThreshold")]
    public long ConfirmThreshold { get; set; }

    [JsonPropertyName("transferFeeBasisPoints")]
    public int TransferFeeBasisPoints { get; set; }

    /// <summary>When the next transfer is allowed by the cooldown / hourly limit; null = now.</summary>
    [JsonPropertyName("nextTransferAt")]
    public DateTime? NextTransferAt { get; set; }

    /// <summary>Old enough and titled enough to send (DESIGN.md §5 Q5).</summary>
    [JsonPropertyName("eligible")]
    public bool Eligible { get; set; }

    [JsonPropertyName("minSenderAccountAgeHours")]
    public int MinSenderAccountAgeHours { get; set; }

    /// <summary>When the account-age rule stops applying; null when already met.</summary>
    [JsonPropertyName("eligibleFrom")]
    public DateTime? EligibleFrom { get; set; }

    [JsonPropertyName("requiredTitleName")]
    public string? RequiredTitleName { get; set; }

    [JsonPropertyName("requiredExperience")]
    public int? RequiredExperience { get; set; }

    /// <summary>Transfer-locked or frozen: can neither send nor receive.</summary>
    [JsonPropertyName("locked")]
    public bool Locked { get; set; }
}

public class LeaderboardEntryDto
{
    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = null!;

    [JsonPropertyName("balance")]
    public long Balance { get; set; }
}

/// <summary>GET api/currency/leaderboard: richest active players, locked accounts and holders of
/// knk.baltop.exempt left out.</summary>
public class LeaderboardDto
{
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = null!;

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("entries")]
    public List<LeaderboardEntryDto> Entries { get; set; } = new();

    [JsonPropertyName("generatedAt")]
    public DateTime GeneratedAt { get; set; }
}

/// <summary>Payload of a PaymentReceived player notification (shown in-game, on the next join
/// when the recipient is offline).</summary>
public class PaymentNotificationDto
{
    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = null!;

    [JsonPropertyName("fromUserId")]
    public int FromUserId { get; set; }

    [JsonPropertyName("fromUsername")]
    public string? FromUsername { get; set; }

    [JsonPropertyName("transactionPublicId")]
    public string TransactionPublicId { get; set; } = null!;

    [JsonPropertyName("balanceAfter")]
    public long BalanceAfter { get; set; }
}
