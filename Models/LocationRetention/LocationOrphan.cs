namespace knkwebapi_v2.Models;

/// <summary>
/// Review state of an orphaned Location (KNG-80, docs/architecture/location-retention.md in
/// knk-workspace). Open → Kept or Deleted by staff; Resolved when the Location stopped being an
/// orphan by itself (a relation or custom name appeared, or it was deleted elsewhere).
/// </summary>
public enum LocationOrphanStatus : byte
{
    Open = 0,
    Kept = 1,
    Deleted = 2,
    Resolved = 3
}

/// <summary>
/// One orphaned Location flagged by a retention run, and what staff decided about it (KNG-80).
/// Never deleted, and LocationId is deliberately not a foreign key: after a Delete this row,
/// with its snapshot and who/when/note, is the audit record of the deleted Location.
/// <para>
/// A Kept item suppresses re-flagging until KeptRecheckMonths have passed or the Location's
/// name/world/coordinates differ from the snapshot; the run then opens a new item pointing at
/// it (PreviousItemId) and sets SupersededByItemId here, so the earlier decision stays visible.
/// </para>
/// </summary>
public class LocationOrphan
{
    public int Id { get; set; }

    /// <summary>The flagged Location. Not a foreign key (the Location may be deleted).</summary>
    public int LocationId { get; set; }

    public LocationOrphanStatus Status { get; set; } = LocationOrphanStatus.Open;

    /// <summary>The run that flagged it, and when.</summary>
    public int? FlaggedByRunId { get; set; }
    public DateTime FlaggedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The last run that still found it orphaned.</summary>
    public int? LastSeenRunId { get; set; }
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    // ===== Snapshot of the Location when flagged =====
    public string? Name { get; set; }
    public string? World { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }

    /// <summary>The Location's CreatedAt; null for Locations created before it was tracked.</summary>
    public DateTime? LocationCreatedAt { get; set; }

    // ===== Decision =====
    public int? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }

    /// <summary>Why a Resolved item is no longer an orphan (or why a delete was refused).</summary>
    public string? ResolvedReason { get; set; }

    /// <summary>The earlier (Kept) item for this Location that this re-flag follows.</summary>
    public int? PreviousItemId { get; set; }

    /// <summary>Set on a Kept item once a later run re-flagged it as a new Open item.</summary>
    public int? SupersededByItemId { get; set; }
}
