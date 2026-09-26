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
