namespace knkwebapi_v2.Realtime.Contracts;

/// <summary>
/// WT2 (PandiO/knk-workspace#181, DESIGN §6.4–§6.5, §8.3–§8.5): world tasks over the hubs. Payloads carry ids and the few
/// fields a display needs, never the task code or the task's output (REST stays the truth: the web app reads
/// <c>GET api/WorldTasks/{id}</c> for those).
/// </summary>
public static class WorldTaskEventTypes
{
    // ---- API → game server ----

    /// <summary>A task is offered to an online player. Payload <see cref="WorldTaskOfferedPayload"/>.</summary>
    public const string WorldTaskOffered = "WorldTaskOffered";

    /// <summary>
    /// A task this server may hold or show ended or was taken back (cancelled in the web app, expired, or released by
    /// the web app's "Replace"). Payload <see cref="WorldTaskCancelledPayload"/>.
    /// </summary>
    public const string WorldTaskCancelled = "WorldTaskCancelled";

    /// <summary>
    /// An offer that is no longer open (claimed with the task code by someone else, or timed out): drop the
    /// Accept/Decline line. Payload <see cref="WorldTaskOfferWithdrawnPayload"/>.
    /// </summary>
    public const string WorldTaskOfferWithdrawn = "WorldTaskOfferWithdrawn";

    /// <summary>A headless task (scan) waits to be claimed. Payload <see cref="WorldTaskCreatedPayload"/>.</summary>
    public const string WorldTaskCreated = "WorldTaskCreated";

    // ---- API → web app ----

    /// <summary>A task changed. To <c>task:{id}</c> and <c>user:{creator}</c>. Payload <see cref="WorldTaskUpdatedPayload"/>.</summary>
    public const string WorldTaskUpdated = "WorldTaskUpdated";

    /// <summary>A Minecraft catalogue scan changed. To <c>user:{starter}</c>. Payload <see cref="CatalogScanUpdatedPayload"/>.</summary>
    public const string CatalogScanUpdated = "CatalogScanUpdated";

    // ---- game server → API (hub invocation) ----

    /// <summary><c>GameServerHub.WorldTaskOfferResult</c>: what the player (or the plugin's checks) answered.</summary>
    public const string WorldTaskOfferResult = "WorldTaskOfferResult";
}

/// <summary>How the plugin presents an offer (DESIGN §8.3).</summary>
public static class WorldTaskOfferModes
{
    /// <summary>The player started the task in the web app: claim it at once ("Send to Minecraft").</summary>
    public const string AutoClaim = "auto-claim";

    /// <summary>Someone else's task ("Hand off to…"): clickable Accept/Decline, timeout 2 min.</summary>
    public const string Accept = "accept";
}

/// <summary>What <c>WorldTaskOfferResult</c> reports.</summary>
public static class WorldTaskOfferResults
{
    public const string Accepted = "accepted";
    public const string Declined = "declined";
    public const string TimedOut = "timed-out";
    /// <summary>The player already has a task open in game (race after the web app's busy check).</summary>
    public const string Busy = "busy";
    /// <summary>The player is not on this server after all.</summary>
    public const string Offline = "offline";
    /// <summary>The player lacks <c>knk.tasks</c>.</summary>
    public const string NoPermission = "no-permission";
}

/// <param name="TaskId">The world task.</param>
/// <param name="Uuid">The player it is offered to (lower case, with dashes as stored).</param>
/// <param name="TaskType">e.g. <c>LocationSelection</c>.</param>
/// <param name="FieldName">The form field the result fills.</param>
/// <param name="Label">What the chat line shows: the entity type and field, e.g. <c>Town › Location</c>.</param>
/// <param name="EntityType">The workflow session's entity type, when known.</param>
/// <param name="Mode"><see cref="WorldTaskOfferModes"/>.</param>
/// <param name="FromUsername">Who sent it (the creator), for an Accept/Decline line.</param>
/// <param name="TimeoutSeconds">How long an Accept/Decline offer stays open.</param>
public sealed record WorldTaskOfferedPayload(int TaskId, string Uuid, string TaskType, string? FieldName, string Label,
    string? EntityType, string Mode, string? FromUsername, int TimeoutSeconds);

/// <param name="Status">The task's status now: <c>Cancelled</c>, <c>Expired</c>, or <c>Pending</c> (handed back by the
/// web app's "Replace").</param>
/// <param name="Uuid">The player who held it or had it offered, when known.</param>
/// <param name="Reason">For the chat line.</param>
public sealed record WorldTaskCancelledPayload(int TaskId, string Status, string? Uuid, string? Reason);

public sealed record WorldTaskOfferWithdrawnPayload(int TaskId, string Uuid, string? Reason);

public sealed record WorldTaskCreatedPayload(int TaskId, string TaskType);

/// <summary>
/// The display fields of a task (DESIGN §6.5). The web app renders status and offer from it and reads
/// <c>GET api/WorldTasks/{id}</c> for the output once the task is Completed.
/// </summary>
public sealed record WorldTaskUpdatedPayload(
    int TaskId,
    string Status,
    string OfferState,
    int? TargetUserId,
    string? TargetUsername,
    string? ClaimedByMinecraftUsername,
    string? ClaimedByServerId,
    string? ErrorMessage,
    int? FormSubmissionProgressId,
    string? FieldName,
    DateTime? ExpiresAt,
    DateTime? OfferedAt,
    int ReleaseCount);

/// <param name="ScanId">The catalogue scan.</param>
/// <param name="WorldTaskId">Its world task.</param>
/// <param name="TaskStatus">The task's status (the scan's own status is read with <c>GET</c>, which derives it).</param>
public sealed record CatalogScanUpdatedPayload(int ScanId, int WorldTaskId, string TaskStatus);

/// <summary><c>GameServerHub.WorldTaskOfferResult</c> argument.</summary>
public sealed class WorldTaskOfferResultDto
{
    public int TaskId { get; set; }

    /// <summary><see cref="WorldTaskOfferResults"/>.</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>The player the offer was for.</summary>
    public string? Uuid { get; set; }
}
