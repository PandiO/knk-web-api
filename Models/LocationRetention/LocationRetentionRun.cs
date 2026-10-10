namespace knkwebapi_v2.Models;

/// <summary>
/// One run of the Location orphan check (KNG-80): scheduled or "Run check now". Kept as the run
/// log the issue asks for; the latest one is shown in the admin panel's header.
/// </summary>
public class LocationRetentionRun
{
    public int Id { get; set; }

    /// <summary>"scheduled" or "manual".</summary>
    public string Trigger { get; set; } = "scheduled";

    /// <summary>Staff member who pressed "Run check now"; null for a scheduled run.</summary>
    public int? TriggeredByUserId { get; set; }

    /// <summary>The schedule slot a scheduled run covers (UTC), so a slot runs once.</summary>
    public DateTime? ScheduledSlotUtc { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public bool Succeeded { get; set; }
    public string? Error { get; set; }

    /// <summary>Locations with a default name created before the grace period, i.e. checked for relations.</summary>
    public int CandidatesScanned { get; set; }
    /// <summary>Orphans found this run (new + already known).</summary>
    public int OrphansFound { get; set; }
    /// <summary>Orphans not flagged before, plus Kept items re-flagged after their recheck period or a change.</summary>
    public int NewOrphans { get; set; }
    public int AlreadyKnown { get; set; }
    /// <summary>Of NewOrphans, how many were Kept items flagged again.</summary>
    public int Reflagged { get; set; }
    /// <summary>Open items that are no longer orphans and were marked Resolved.</summary>
    public int ResolvedCount { get; set; }
    public long DurationMs { get; set; }

    /// <summary>When the in-game digest was queued; null when there was nothing new.</summary>
    public DateTime? DigestQueuedAt { get; set; }
}
