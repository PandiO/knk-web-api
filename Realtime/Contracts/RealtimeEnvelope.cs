namespace knkwebapi_v2.Realtime.Contracts;

/// <summary>
/// Every server-to-client message (DESIGN §4.2): one hub method per event type, with this single argument.
/// Serialized by the SignalR JSON protocol in camelCase: <c>{ v, seq, type, at, actor, payload }</c>.
/// </summary>
/// <param name="V">Contract version of <paramref name="Type"/>; a receiver ignores a type or version it doesn't know.
/// A breaking change gets a new type name.</param>
/// <param name="Seq">Per connection, starts at 1 and goes up by one per message sent to that connection. A receiver
/// that sees a jump runs its full resync. Not persisted.</param>
/// <param name="Type">The event type, equal to the hub method name.</param>
/// <param name="At">When the event was published (UTC).</param>
/// <param name="Actor">Who caused it, when known.</param>
/// <param name="Payload">Small "this changed" data: ids and the few fields a display needs. REST stays the truth.</param>
public sealed record RealtimeEnvelope(int V, long Seq, string Type, DateTime At, RealtimeActor? Actor, object? Payload);

/// <param name="UserId">The web user or staff member behind the change, if any.</param>
/// <param name="Source"><c>web</c>, <c>plugin</c> or <c>system</c>.</param>
public sealed record RealtimeActor(int? UserId, string Source)
{
    public static readonly RealtimeActor System = new(null, "system");
}

/// <summary>The event types and their current versions. DESIGN §6 is the human-readable catalogue.</summary>
public static class RealtimeEventTypes
{
    /// <summary>
    /// First message on every game-server connection (seq 1): confirms the server id the API filed it under.
    /// Payload <see cref="WelcomePayload"/>.
    /// </summary>
    public const string Welcome = "Welcome";

    // ---- S2, user state (DESIGN §6.1, PandiO/knk-workspace#179). All to the game servers. ----

    /// <summary>A user's memberships or direct grants changed. Payload <see cref="UserPermissionsChangedPayload"/>.</summary>
    public const string UserPermissionsChanged = "UserPermissionsChanged";

    /// <summary>A permission group, or its node grants, changed. Payload <see cref="PermissionGroupChangedPayload"/>.</summary>
    public const string PermissionGroupChanged = "PermissionGroupChanged";

    /// <summary>Coins, gems or experience changed. Payload <see cref="UserBalancesChangedPayload"/>.</summary>
    public const string UserBalancesChanged = "UserBalancesChanged";

    /// <summary>Frozen or unfrozen by staff. Payload <see cref="UserFrozenChangedPayload"/>.</summary>
    public const string UserFrozenChanged = "UserFrozenChanged";

    /// <summary>Owner/staff mode (vanish) changed. Payload <see cref="UserModeChangedPayload"/>.</summary>
    public const string UserModeChanged = "UserModeChanged";

    /// <summary>Other cached user fields changed (multipliers, gate method, link, merge). Payload <see cref="UserChangedPayload"/>.</summary>
    public const string UserChanged = "UserChanged";

    /// <summary>
    /// One queued player notification (the hub-backed <c>IPlayerNotificationQueue</c>, DESIGN §4.3). Payload: the
    /// same <c>PlayerNotificationDto</c> <c>GET api/PlayerNotifications/pending</c> returns; the plugin acknowledges it
    /// over REST as before.
    /// </summary>
    public const string PlayerNotification = "PlayerNotification";
}

/// <summary>Why <see cref="RealtimeEventTypes.UserPermissionsChanged"/> was sent (informational; the handler is the same).</summary>
public static class UserPermissionsChangeReasons
{
    public const string GroupAssigned = "GroupAssigned";
    public const string GroupRemoved = "GroupRemoved";
    public const string RankExpired = "RankExpired";
    public const string GrantChanged = "GrantChanged";
}

/// <summary>What happened to the group in <see cref="PermissionGroupChangedPayload"/>.</summary>
public static class PermissionGroupChangeOps
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";
    /// <summary>A node grant held by the group was added, changed or removed.</summary>
    public const string GrantsChanged = "GrantsChanged";
}

/// <param name="UserId">The knk user.</param>
/// <param name="Uuid">Their Minecraft UUID (events are only sent for users who have one).</param>
/// <param name="Reason">See <see cref="UserPermissionsChangeReasons"/>.</param>
public sealed record UserPermissionsChangedPayload(int UserId, string Uuid, string Reason);

/// <param name="GroupId">The permission group.</param>
/// <param name="Op">See <see cref="PermissionGroupChangeOps"/>.</param>
public sealed record PermissionGroupChangedPayload(int GroupId, string Op);

/// <summary>The balances after the change (committed values), so the receiver needs no re-read.</summary>
public sealed record UserBalancesChangedPayload(int UserId, string Uuid, long Coins, long Gems, long Xp);

/// <param name="Frozen">The new state.</param>
/// <param name="Reason">The freeze reason (null on unfreeze).</param>
/// <param name="By">The staff member's user id, when known.</param>
public sealed record UserFrozenChangedPayload(int UserId, string Uuid, bool Frozen, string? Reason, int? By);

/// <param name="Mode"><c>None</c>, <c>Staff</c> or <c>Owner</c> (the <c>ActiveMode</c> names).</param>
public sealed record UserModeChangedPayload(int UserId, string Uuid, string Mode);

/// <param name="Fields">Which fields changed, e.g. <c>multipliers</c>, <c>gatePassThroughMethod</c>, <c>link</c>,
/// <c>merge</c>. The receiver drops its cached user and re-reads it.</param>
public sealed record UserChangedPayload(int UserId, string Uuid, IReadOnlyList<string> Fields);

/// <param name="ServerId">The id from the <c>X-Knk-Server-Id</c> header, as the API stored it.</param>
/// <param name="PresenceGraceSeconds">How long the API keeps this server's players online after a disconnect.</param>
public sealed record WelcomePayload(string ServerId, int PresenceGraceSeconds);
