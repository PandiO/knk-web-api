using System;
using System.ComponentModel.DataAnnotations;

namespace knkwebapi_v2.Models;

public class WorldTask
{
    public int Id { get; set; }

    // Association to the workflow
    public int WorkflowSessionId { get; set; }
    public WorkflowSession WorkflowSession { get; set; } = null!;

    // Step mapping
    public int? StepNumber { get; set; }
    public string? StepKey { get; set; }
    public string? FieldName { get; set; }

    /// <summary>
    /// PandiO/knk-workspace#180 (DESIGN §8.7): the form draft this task fills a field of, so a reload, a step change or
    /// a resumed draft finds the task again (GET api/FormSubmissionProgress/{id}/world-tasks). Null for tasks that
    /// belong to no draft (catalogue scans, older rows).
    /// </summary>
    public int? FormSubmissionProgressId { get; set; }
    public FormSubmissionProgress? FormSubmissionProgress { get; set; }

    /// <summary>#180: when the web app wrote the result into the draft (POST api/WorldTasks/{id}/applied).</summary>
    public DateTime? AppliedAt { get; set; }

    public string TaskType { get; set; } = null!; // e.g., "LocationSelection", "WgRegionId", "GateBlockScan"

    /// <summary>
    /// <see cref="WorldTaskStatuses"/>: Pending, InProgress, Completed, Failed, Cancelled, Expired. Transitions are
    /// enforced by <c>WorldTaskService</c> (#180) and are conditional on the stored status (concurrency token).
    /// </summary>
    public string Status { get; set; } = WorldTaskStatuses.Pending;

    /// <summary>
    /// The task code a player types in game (<c>/knk task &lt;code&gt;</c>; the column keeps its old name). 6 characters
    /// from A-Z0-9 made with <c>RandomNumberGenerator</c>, unique among open tasks (#180). A closed task keeps its code
    /// for the record, but lookups and claims ignore it.
    /// </summary>
    public string? LinkCode { get; set; }

    // Who should handle this task
    public int? AssignedUserId { get; set; }
    public User? AssignedUser { get; set; }

    /// <summary>#180: the web user who created the task (from the JWT; the session owner when the game server creates one).</summary>
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    /// <summary>#180/#181: the player the task is offered to (null = task code only). Set by the WT2 delivery (DESIGN §8.3).</summary>
    public int? TargetUserId { get; set; }
    public User? TargetUser { get; set; }

    /// <summary>#181: <see cref="WorldTaskOfferStates"/>.</summary>
    public string OfferState { get; set; } = WorldTaskOfferStates.None;
    public DateTime? OfferedAt { get; set; }

    // Server and player claim info
    public string? ClaimedByServerId { get; set; }
    public string? ClaimedByMinecraftUsername { get; set; }
    /// <summary>#180: the claiming player's Minecraft UUID, and their user when the UUID is linked to one.</summary>
    public string? ClaimedByUuid { get; set; }
    public int? ClaimedByUserId { get; set; }
    public User? ClaimedByUser { get; set; }

    /// <summary>#180: a Pending task past this time is Expired by the sweep (default 30 min after create or release).</summary>
    public DateTime? ExpiresAt { get; set; }
    /// <summary>#180: an InProgress task whose claim is not renewed (heartbeat) by this time is released to Pending.</summary>
    public DateTime? LeaseUntil { get; set; }
    /// <summary>#180: how many times the task went back to Pending (in-game cancel, quit, restart, lease expiry).</summary>
    public int ReleaseCount { get; set; }

    // Flexible payload for input/output data
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public string? ErrorMessage { get; set; }

    // Legacy support for existing code
    [Obsolete("Use InputJson/OutputJson instead")]
    public string? PayloadJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClaimedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>#180: optimistic concurrency, next to the status token (a heartbeat between read and write wins over a sweep).</summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }
}

/// <summary>PandiO/knk-workspace#180: world task statuses and the transitions the state machine allows (DESIGN §8.2).</summary>
public static class WorldTaskStatuses
{
    public const string Pending = "Pending";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string Expired = "Expired";

    public static readonly string[] All = { Pending, InProgress, Completed, Failed, Cancelled, Expired };
    /// <summary>Statuses a task can still move out of; their task codes are live.</summary>
    public static readonly string[] Open = { Pending, InProgress };

    public static bool IsOpen(string? status) => status == Pending || status == InProgress;

    /// <summary>The canonical spelling of a status, or null when it is not one ("pending" → "Pending").</summary>
    public static string? Normalize(string? status) =>
        All.FirstOrDefault(s => string.Equals(s, status?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <code>
    /// Pending ──claim──► InProgress ──complete──► Completed
    ///    │                  ├──fail────────────► Failed
    ///    │                  ├──release/lease───► Pending
    ///    ├──cancel──────────┴─────────────────► Cancelled
    ///    └──ExpiresAt passed──────────────────► Expired
    /// </code>
    /// </summary>
    public static bool CanTransition(string from, string to) => (from, to) switch
    {
        (Pending, InProgress) => true,
        (Pending, Cancelled) => true,
        (Pending, Expired) => true,
        (InProgress, Completed) => true,
        (InProgress, Failed) => true,
        (InProgress, Pending) => true,
        (InProgress, Cancelled) => true,
        _ => false
    };
}

/// <summary>#181 (WT2) offer states; the columns exist since #180 so WT2 needs no further migration (string column, 20 chars).</summary>
public static class WorldTaskOfferStates
{
    public const string None = "None";
    public const string Offered = "Offered";
    public const string Accepted = "Accepted";
    public const string Declined = "Declined";
    public const string TimedOut = "TimedOut";

    /// <summary>WT2: waits until the target's current task ends (busy check "Queue").</summary>
    public const string Queued = "Queued";

    /// <summary>WT2: the target already had a task open when it arrived (a race after the busy check).</summary>
    public const string Busy = "Busy";
}
