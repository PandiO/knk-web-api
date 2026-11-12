namespace knkwebapi_v2.Realtime.Contracts;

/// <summary>
/// S4 (PandiO/knk-workspace#183, DESIGN §6.5, §9): the web hub's live-update events. They go only to web connections
/// whose user holds the node that already gates the same data over REST (<see cref="RealtimeWebChangeMap"/>), checked
/// when each message is sent (<see cref="RealtimeWebAudience"/>). Both are doorbells: the page re-reads over REST.
/// </summary>
public static class WebLiveEventTypes
{
    /// <summary>
    /// An entity the dashboard or detail page can show was created, changed or deleted. Same type name as the game
    /// servers' <see cref="ConfigEventTypes.EntityChanged"/>, other payload (<see cref="WebEntityChangedPayload"/>),
    /// other hub.
    /// </summary>
    public const string EntityChanged = ConfigEventTypes.EntityChanged;

    /// <summary>A player went online or offline in game. Payload <see cref="PresenceChangedPayload"/>.</summary>
    public const string PresenceChanged = "PresenceChanged";
}

/// <param name="Type">The entity type name the dashboard uses (the model class, e.g. <c>Town</c>; a <c>Town</c> also
/// sends one for <c>Domain</c>).</param>
/// <param name="Id">The entity's id; null when many of that type changed in one save, or the type has no single id
/// ("re-read the list").</param>
/// <param name="Op"><see cref="EntityChangeOps"/>.</param>
/// <param name="ActorUserId">The web user who saved it; null for the game server or a background job.</param>
/// <param name="ActorName">That user's name (the "updated by" hint), from their access token.</param>
public sealed record WebEntityChangedPayload(string Type, int? Id, string Op, int? ActorUserId, string? ActorName);

/// <param name="UserId">The player's user id; null when many changed at once (a roster reconcile): re-read.</param>
/// <param name="Online">Online now; null together with a null <paramref name="UserId"/>.</param>
/// <param name="ServerId">The game server they are on (online only, when the hub knows it).</param>
/// <param name="LastSeenAt">The new <c>User.LastSeenAt</c> (UTC).</param>
public sealed record PresenceChangedPayload(int? UserId, bool? Online, string? ServerId, DateTime? LastSeenAt);
