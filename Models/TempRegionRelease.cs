using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// PandiO/knk-workspace#269: a request to the game server to remove the <c>tempregion_worldtask_&lt;id&gt;</c> region a
/// world task drew, because nothing will use it any more: its draft was deleted (cancelled or discarded in the web app),
/// the task closed without a completed result (cancelled, expired, failed), or the world-task sweep removed its workflow
/// session. The game server lists the rows (<c>GET api/WorldTasks/temp-region-releases</c>), removes the region after its
/// own checks and settles the row, which deletes it. The API lists a row only once nothing it knows of uses the region
/// (<see cref="Services.TempRegionReleaseService"/>); the plugin's 14-day temp-region cleanup stays as the backstop.
/// No foreign key to <c>world_tasks</c>: the row outlives a task the sweep removes with its session.
/// </summary>
public class TempRegionRelease
{
    public int Id { get; set; }

    /// <summary>The world task that drew the region; the region is <c>tempregion_worldtask_&lt;WorldTaskId&gt;</c>.</summary>
    public int WorldTaskId { get; set; }

    /// <summary><c>tempregion_worldtask_&lt;WorldTaskId&gt;</c>, stored so the row reads on its own.</summary>
    public string RegionId { get; set; } = null!;

    /// <summary>The world from the task's result (<c>worldName</c>), or null when the task has none (search every world).</summary>
    public string? World { get; set; }

    /// <summary>The game server that held the task (<c>ClaimedByServerId</c>), or null when unknown (any server may settle it).</summary>
    public string? ServerId { get; set; }

    /// <summary><see cref="TempRegionReleaseReasons"/>.</summary>
    public string Reason { get; set; } = null!;

    public DateTime RequestedAt { get; set; }
}

/// <summary>#269: why a temp region was released.</summary>
public static class TempRegionReleaseReasons
{
    public const string DraftDeleted = "DraftDeleted";
    public const string TaskCancelled = "TaskCancelled";
    public const string TaskExpired = "TaskExpired";
    public const string TaskFailed = "TaskFailed";
    public const string SessionRemoved = "SessionRemoved";

    /// <summary>The reason for a task that closed with <paramref name="status"/>, or null when that status keeps its result.</summary>
    public static string? ForClosedStatus(string? status) => status switch
    {
        WorldTaskStatuses.Cancelled => TaskCancelled,
        WorldTaskStatuses.Expired => TaskExpired,
        WorldTaskStatuses.Failed => TaskFailed,
        _ => null
    };
}

/// <summary>#269: what the game server did with a released temp region.</summary>
public static class TempRegionReleaseOutcomes
{
    /// <summary>The region was removed.</summary>
    public const string Removed = "Removed";
    /// <summary>No such region on the server (already removed, renamed, or never drawn).</summary>
    public const string Missing = "Missing";
    /// <summary>The server's own checks keep it (a domain or lootbox area uses it, no creation timestamp, ...).</summary>
    public const string Kept = "Kept";

    public static readonly string[] All = { Removed, Missing, Kept };

    public static string? Normalize(string? outcome) =>
        All.FirstOrDefault(o => string.Equals(o, outcome?.Trim(), StringComparison.OrdinalIgnoreCase));
}
