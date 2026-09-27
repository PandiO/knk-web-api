using System;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// A player transfer waiting for the sender's confirmation (currency DESIGN.md §3.4/§3.6,
/// amount ≥ CurrencyPolicy.ConfirmThreshold). Schema only in Phase 1; used from Phase 3.
/// </summary>
public class CurrencyPendingTransfer
{
    public long Id { get; set; }

    public string PublicId { get; set; } = null!;

    public int SenderUserId { get; set; }

    public int RecipientUserId { get; set; }

    public Currency Currency { get; set; }

    public long Amount { get; set; }

    public string? Note { get; set; }

    /// <summary>The creating request's key; unique.</summary>
    public string IdempotencyKey { get; set; } = null!;

    public CurrencyPendingTransferStatus Status { get; set; } = CurrencyPendingTransferStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    /// <summary>The posted transfer once confirmed.</summary>
    public long? ResultTransactionId { get; set; }
}

public enum CurrencyPendingTransferStatus : byte
{
    Pending = 0,
    Confirmed = 1,
    Cancelled = 2,
    Expired = 3
}
