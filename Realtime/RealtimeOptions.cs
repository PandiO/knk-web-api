namespace knkwebapi_v2.Realtime;

/// <summary>
/// Host-level knobs of the realtime channel (PandiO/knk-workspace#178, docs/specs/realtime-signalr/DESIGN.md §4–§5),
/// section "Realtime" in appsettings. Every value has a default, so the section is optional. The game server only
/// connects when its own <c>realtime.enabled</c> is on; without a connected server the hubs are idle.
/// </summary>
public sealed class RealtimeOptions
{
    public const string SectionName = "Realtime";

    /// <summary>Messages waiting to be sent; when full the oldest is dropped (and logged), never the request.</summary>
    public int ChannelCapacity { get; set; } = 1000;

    /// <summary>
    /// How long a game server may stay disconnected before its players are marked offline (DESIGN §5, Q5: 60 s).
    /// </summary>
    public int PresenceGraceSeconds { get; set; } = 60;

    /// <summary>How often the grace period is checked.</summary>
    public int PresenceSweepSeconds { get; set; } = 10;

    /// <summary>SignalR keep-alive ping (DESIGN §4.1: the SignalR default, 15 s).</summary>
    public int KeepAliveSeconds { get; set; } = 15;

    /// <summary>A client that sent nothing for this long is dropped (DESIGN §4.1: the SignalR default, 30 s).</summary>
    public int ClientTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Game server ids that may connect (DESIGN §7.3). Empty (the default, one game server) accepts any valid id and
    /// logs it.
    /// </summary>
    public string[] AllowedServerIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// S4 (#183): how long a web user's "holds node X" answer is reused for the node-gated live updates
    /// (<c>EntityChanged</c>, <c>PresenceChanged</c>). A node taken away stops those events within this time; 0 checks
    /// on every message.
    /// </summary>
    public int WebPermissionCacheSeconds { get; set; } = 30;

    /// <summary>
    /// S4 (#183, developer decision 2026-11-06 on LX1-10): the web hub's live updates. False: no <c>EntityChanged</c> or
    /// <c>PresenceChanged</c> web event is built or sent (the web app then behaves as before S4: no dashboard refresh,
    /// no live badges). The game servers' events, the web hub itself and the profile summary's <c>presence</c> field
    /// stay. Read on every save, so a changed appsettings file applies without a restart; an environment variable
    /// (<c>Realtime__WebLiveUpdatesEnabled</c>) needs one.
    /// </summary>
    public bool WebLiveUpdatesEnabled { get; set; } = true;
}
