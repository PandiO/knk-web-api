using System;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// A player's GDPR erasure request (KNG-34 D12, DESIGN.md §F.14, developer decisions 2026-10-03).
/// Filed by the player on the web app (confirmed through an emailed link) or by staff/the owner for a
/// player (no email step). Once confirmed it runs automatically at <see cref="ScheduledAt"/> — the
/// end of a grace period (5 days) in which the player or staff can cancel it. Keeps only counts of
/// what was removed.
/// </summary>
public class PrivacyDeletionRequest
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public PrivacyRequestSource Source { get; set; }

    public DateTime RequestedAt { get; set; }

    /// <summary>Legal deadline (Art. 12(3)): one month from the confirmed request.</summary>
    public DateTime DueAt { get; set; }

    public PrivacyRequestStatus Status { get; set; }

    public int RequestedByUserId { get; set; }

    public string? Note { get; set; }

    /// <summary>SHA-256 (hex) of the emailed confirmation token; null once used or for staff/owner requests.</summary>
    public string? ConfirmationTokenHash { get; set; }

    public DateTime? ConfirmationExpiresAt { get; set; }

    /// <summary>When the player confirmed by email, or when staff/the owner filed the request.</summary>
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>When the erasure runs: ConfirmedAt + the grace period.</summary>
    public DateTime? ScheduledAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public int? CancelledByUserId { get; set; }

    public DateTime? ExecutedAt { get; set; }

    /// <summary>Null when the scheduled job executed the request.</summary>
    public int? ExecutedByUserId { get; set; }

    /// <summary>Counts per table only (JSON object), never the removed data.</summary>
    public string? ResultJson { get; set; }
}
