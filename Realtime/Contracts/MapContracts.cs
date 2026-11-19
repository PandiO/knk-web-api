namespace knkwebapi_v2.Realtime.Contracts;

/// <summary>
/// Overview map L1 (PandiO/knk-workspace#735, OVERVIEW_MAP_DESIGN § 6.2.6): the argument of the game hub's
/// <c>ReportRegionOutlines</c>. The plugin sends it (only with its <c>map.reporting.enabled</c> on) after its startup
/// reconcile with every domain region it found, and after a domain region is created, redefined or renamed with that
/// one region; a rename also names the old region in <see cref="Removed"/>.
/// </summary>
public sealed class RegionOutlineReportDto
{
    /// <summary>Outlines to store or replace (keyed by world and region id).</summary>
    public List<RegionOutlineDto>? Outlines { get; set; }

    /// <summary>Regions that no longer exist (the old name of a renamed region): their rows are deleted.</summary>
    public List<RegionOutlineRefDto>? Removed { get; set; }
}

/// <summary>One region's outline.</summary>
public sealed class RegionOutlineDto
{
    public string WgRegionId { get; set; } = string.Empty;

    public string WorldName { get; set; } = string.Empty;

    /// <summary>The outline's points in order (block x and z); at least three.</summary>
    public List<OutlinePointDto>? Points { get; set; }

    public int MinY { get; set; }

    public int MaxY { get; set; }
}

/// <summary>A point of an outline: block x and z.</summary>
public sealed class OutlinePointDto
{
    public int X { get; set; }

    public int Z { get; set; }
}

/// <summary>A region by world and id.</summary>
public sealed class RegionOutlineRefDto
{
    public string WgRegionId { get; set; } = string.Empty;

    public string WorldName { get; set; } = string.Empty;
}

/// <summary>
/// Overview map L4 (PandiO/knk-workspace#738, OVERVIEW_MAP_DESIGN § 5 rule 6, § 6.2.6): the argument of the game hub's
/// <c>ReportMapPlayers</c>. The plugin sends it (only with its <c>map.reporting.enabled</c> on) every
/// <c>map.reporting.interval-ticks</c> with its online players; a large server splits it into several messages. The API
/// keeps each player's latest entry and drops it on <c>PlayerLeft</c> or after three missed intervals.
/// </summary>
public sealed class MapPlayersReportDto
{
    /// <summary>The plugin's report interval in milliseconds (the "three missed intervals" rule).</summary>
    public int IntervalMs { get; set; }

    public List<MapPlayerReportDto>? Players { get; set; }
}

/// <summary>One online player as reported. Never served as is: <c>GET api/Map/players</c> builds its own DTOs.</summary>
public sealed class MapPlayerReportDto
{
    public string Uuid { get; set; } = string.Empty;

    public string World { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    public double Z { get; set; }

    public double Yaw { get; set; }

    public bool Afk { get; set; }

    /// <summary><c>none</c>, <c>staff</c> or <c>owner</c>; anything else counts as hidden.</summary>
    public string? Mode { get; set; }

    /// <summary>The short key of the running minigame the player plays in (<c>siege</c>), or null.</summary>
    public string? Minigame { get; set; }

    /// <summary>The API id of that match (a <c>SiegeMatch</c> for a siege), or null while the plugin does not know it.</summary>
    public long? MatchId { get; set; }
}

/// <summary>
/// Overview map L7 (PandiO/knk-workspace#741, OVERVIEW_MAP_DESIGN § 5 rule 11, § 6.2.5–6.2.6, O17): live gate commands.
/// API → game server <see cref="GateCommand"/> (payload <see cref="GateCommandPayload"/>, v1); game server → API the hub
/// method <c>GameServerHub.GateCommandResult</c> (argument <see cref="GateCommandResultDto"/>).
/// </summary>
public static class GateCommandEventTypes
{
    /// <summary>Run a gate command now with the normal animation. Sent to every connected game server.</summary>
    public const string GateCommand = "GateCommand";

    /// <summary><c>GameServerHub.GateCommandResult</c>: what the game server did.</summary>
    public const string GateCommandResult = "GateCommandResult";
}

/// <summary>The actions of a gate command (lower case on the wire).</summary>
public static class GateCommandActions
{
    public const string Open = "open";
    public const string Close = "close";
    public const string Toggle = "toggle";
    public const string Repair = "repair";

    public static readonly IReadOnlyList<string> All = new[] { Open, Close, Toggle, Repair };

    /// <summary>The action in lower case, or null when it is not one of <see cref="All"/>.</summary>
    public static string? Normalize(string? action)
    {
        var lower = action?.Trim().ToLowerInvariant();
        return lower != null && All.Contains(lower) ? lower : null;
    }
}

/// <summary>What a gate command ended with (the <c>result</c> of the endpoint and of the hub result).</summary>
public static class GateCommandOutcomes
{
    /// <summary>The game server ran it (doors already in the asked state count as done).</summary>
    public const string Done = "done";
    /// <summary>The game server refused it (for example a destroyed door can't open, the gate isn't loaded); see the message.</summary>
    public const string Refused = "refused";
    /// <summary>A siege match runs on the gate (API or game server check); nothing changed.</summary>
    public const string Siege = "siege";
    /// <summary>No game server was connected; nothing was sent.</summary>
    public const string NoGameServer = "no-game-server";
    /// <summary>No result within <c>Map:GateCommands:TimeoutSeconds</c>; the game server may still have run it.</summary>
    public const string Timeout = "timeout";
}

/// <param name="CommandId">Unique per command (a GUID, also in the audit entry): the result names it; a receiver acts
/// once per id.</param>
/// <param name="GateId">The gate structure.</param>
/// <param name="DoorId">One door of that gate, or null for every door of the gate.</param>
/// <param name="Action">See <see cref="GateCommandActions"/>.</param>
/// <param name="StaffUserId">Who sent it.</param>
/// <param name="StaffUsername">Their username, for the game server's log.</param>
public sealed record GateCommandPayload(string CommandId, int GateId, int? DoorId, string Action, int StaffUserId,
    string StaffUsername);

/// <summary>The argument of <c>GameServerHub.GateCommandResult</c>.</summary>
public sealed class GateCommandResultDto
{
    public string CommandId { get; set; } = string.Empty;

    /// <summary><see cref="GateCommandOutcomes.Done"/>, <see cref="GateCommandOutcomes.Refused"/> or
    /// <see cref="GateCommandOutcomes.Siege"/>.</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>A short human-readable line (shown in the panel and audited).</summary>
    public string? Message { get; set; }

    /// <summary>The doors the command acted on, with their state right after it (an animation may still run).</summary>
    public List<GateCommandDoorStateDto>? Doors { get; set; }
}

/// <summary>One door's state as the game server sees it right after the command.</summary>
public sealed class GateCommandDoorStateDto
{
    public int DoorId { get; set; }

    /// <summary><c>open</c>, <c>closed</c>, <c>opening</c>, <c>closing</c> (lower case).</summary>
    public string? State { get; set; }

    public double HealthCurrent { get; set; }

    public double HealthMax { get; set; }

    public bool Destroyed { get; set; }

    /// <summary>What happened to this door: <c>started</c>, <c>already</c>, <c>repaired</c> or <c>skipped</c>.</summary>
    public string? Outcome { get; set; }

    /// <summary>Why it was skipped, when it was.</summary>
    public string? Reason { get; set; }
}
