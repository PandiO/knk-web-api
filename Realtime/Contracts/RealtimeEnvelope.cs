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
}

/// <param name="ServerId">The id from the <c>X-Knk-Server-Id</c> header, as the API stored it.</param>
/// <param name="PresenceGraceSeconds">How long the API keeps this server's players online after a disconnect.</param>
public sealed record WelcomePayload(string ServerId, int PresenceGraceSeconds);
