using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// An anomaly finding of the currency monitor (currency DESIGN.md §3.9, rules R1–R9), written by
/// CurrencyAlertService (currency Phase 5). Staff acknowledge it on the web alerts page; the
/// finding itself is never changed or deleted, and nothing is ever corrected automatically (R1
/// only switches player transfers off).
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

    /// <summary>One line for staff, e.g. "alice got 7 transfers from 5 new accounts in 24 h".</summary>
    public string Summary { get; set; } = "";

    /// <summary>
    /// What makes two findings "the same" (e.g. "R3:user:12"): a finding whose rule and key
    /// already have an alert within the rule's suppression window isn't stored again, so a
    /// condition that lasts doesn't raise an alert every monitor cycle.
    /// </summary>
    public string? DedupKey { get; set; }

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
