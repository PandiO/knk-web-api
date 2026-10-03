using System;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// A player's GDPR erasure request, recorded by the owner (KNG-34 D12, DESIGN.md §F.14,
/// IMPLEMENTATION_PLAN.md §1.3). Due 30 days after the request (Art. 12(3)); executed by the owner
/// or automatically shortly before the due date. Keeps only counts of what was removed.
/// </summary>
public class PrivacyDeletionRequest
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public DateTime RequestedAt { get; set; }

    public DateTime DueAt { get; set; }

    public PrivacyRequestStatus Status { get; set; }

    public int RequestedByUserId { get; set; }

    public string? Note { get; set; }

    public DateTime? ExecutedAt { get; set; }

    /// <summary>Null when the due-date job executed the request.</summary>
    public int? ExecutedByUserId { get; set; }

    /// <summary>Counts per table only (JSON object), never the removed data.</summary>
    public string? ResultJson { get; set; }
}
