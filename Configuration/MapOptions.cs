namespace knkwebapi_v2.Configuration;

/// <summary>
/// The public overview map (PandiO/knk-workspace#732, phase 2 #736), section "Map" in appsettings. Every value has a
/// default, so the section is optional. With <see cref="Enabled"/> false (the default, O27) every <c>api/Map</c> route
/// answers 404 as if it did not exist (spec § 5 rule 15, A03).
/// See knk-workspace <c>docs/specs/in-game-maps/OVERVIEW_MAP_DESIGN.md</c> § 6.2.
/// </summary>
public class MapOptions
{
    public const string SectionName = "Map";

    /// <summary><c>Map:Enabled</c>: the public map endpoints and the web app's map page. Off by default (O27).</summary>
    public bool Enabled { get; set; }

    /// <summary><c>Map:Labels:&lt;Kind&gt;:MaxDistance</c>: from how far away the bridge shows a pin's label (O19).</summary>
    public MapLabelOptions Labels { get; set; } = new();

    /// <summary><c>Map:Flash</c>: how a clicked town's outline flashes (spec § 5 rule 5).</summary>
    public MapFlashOptions Flash { get; set; } = new();

    /// <summary><c>Map:Markers</c>: caching of <c>GET api/Map/markers</c>.</summary>
    public MapMarkerOptions Markers { get; set; } = new();

    /// <summary><c>Map:LivePlayers</c>: live players and arena pins (phase 4a, #738). Off by default (O27).</summary>
    public MapLivePlayersOptions LivePlayers { get; set; } = new();
}

/// <summary>
/// <c>Map:LivePlayers</c> (PandiO/knk-workspace#738, OVERVIEW_MAP_DESIGN § 5 rules 6-9, O07, O21, O27). With
/// <see cref="Enabled"/> false (the default) <c>GET api/Map/players</c> answers 404 and the game hub ignores the
/// plugin's player reports, so nothing is kept.
/// </summary>
public class MapLivePlayersOptions
{
    /// <summary><c>Map:LivePlayers:Enabled</c>: the live player layer. Off by default (O27).</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// <c>Map:LivePlayers:PollIntervalMs</c>: how often the page polls, and the snapshot tick: the three audience
    /// snapshots are built at most once per interval (O21). Default 2000; clamped to 500..60000.
    /// </summary>
    public int PollIntervalMs { get; set; } = 2000;

    /// <summary>
    /// <c>Map:LivePlayers:LabelCacheSeconds</c>: how long a player's name label (rank tag, title, colours) is reused
    /// before it is read again. Default 60; 0 reads it at every snapshot.
    /// </summary>
    public int LabelCacheSeconds { get; set; } = 60;

    /// <summary>The poll interval and snapshot tick as used.</summary>
    public int EffectivePollIntervalMs => Math.Clamp(PollIntervalMs, 500, 60_000);
}

public class MapLabelOptions
{
    /// <summary>Town labels show at every zoom (O19): 0 means no limit.</summary>
    public MapLabelDistance Town { get; set; } = new() { MaxDistance = 0 };

    public MapLabelDistance District { get; set; } = new() { MaxDistance = 1500 };

    public MapLabelDistance Structure { get; set; } = new() { MaxDistance = 600 };

    /// <summary>Minigame arena pins (O24); the pins themselves come with live players (phase 4a, #738).</summary>
    public MapLabelDistance Arena { get; set; } = new() { MaxDistance = 1500 };
}

public class MapLabelDistance
{
    /// <summary>BlueMap camera distance in blocks beyond which the label is hidden; 0 or less means always shown.</summary>
    public double MaxDistance { get; set; }
}

public class MapFlashOptions
{
    /// <summary>How many times a clicked town's outline flashes (default 3).</summary>
    public int Count { get; set; } = 3;

    /// <summary>How long each flash lasts, in milliseconds (default 400).</summary>
    public int DurationMs { get; set; } = 400;
}

public class MapMarkerOptions
{
    /// <summary>
    /// How long a built marker set is reused before it is read from the database again (default 30 s; 0 builds it on
    /// every request). A changed domain or outline shows, with a new marker version, within this time.
    /// </summary>
    public int CacheSeconds { get; set; } = 30;
}
