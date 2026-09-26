using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// An anomaly finding of the currency monitor (currency DESIGN.md §3.9, rules R1–R9). Schema
/// only in Phase 1; written from Phase 5.
/// </summary>
public class CurrencyAlert
{
    public long Id { get; set; }

    /// <summary>Rule id, e.g. "R1".</summary>
    public string Rule { get; set; } = null!;

    public CurrencyAlertSeverity Severity { get; set; }

    public int? UserId { get; set; }

    public long? TransactionId { get; set; }

    public string? DetailsJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int? AckedByUserId { get; set; }

    public DateTime? AckedAt { get; set; }
}

public enum CurrencyAlertSeverity : byte
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}
