namespace knkwebapi_v2.Realtime.Contracts;

/// <summary>
/// PandiO/knk-workspace#988: questions the API asks a game server about a WorldGuard region, with an answer over the game
/// hub, as gate commands do (<see cref="GateCommandEventTypes"/>). API → game server <see cref="RegionGeometryQuery"/>
/// (payload <see cref="RegionQueryPayload"/>, v1) and <see cref="SpotSiteCheckQuery"/> (same payload, v1), sent to every
/// connected game server; game server → API the hub methods <c>GameServerHub.RegionGeometryResult</c> (argument
/// <see cref="RegionGeometryResultDto"/>) and <c>GameServerHub.SpotSiteCheckResult</c> (argument
/// <see cref="SpotSiteCheckResultDto"/>). Nothing in the world changes. Switched with <c>RegionQueries:Enabled</c>.
/// </summary>
public static class RegionQueryEventTypes
{
    /// <summary>The region's shape: world, kind, min and max corner, points. Generic: any validator may ask it. Always answered.</summary>
    public const string RegionGeometryQuery = "RegionGeometryQuery";

    /// <summary><c>GameServerHub.RegionGeometryResult</c>.</summary>
    public const string RegionGeometryResult = "RegionGeometryResult";

    /// <summary>A siege weapon spot's site check (knk-core <c>SpotSiteCheck</c>): floor solid, space clear. Answered
    /// only by a game server with <c>siege.weapons.enabled</c> on.</summary>
    public const string SpotSiteCheckQuery = "SpotSiteCheckQuery";

    /// <summary><c>GameServerHub.SpotSiteCheckResult</c>.</summary>
    public const string SpotSiteCheckResult = "SpotSiteCheckResult";
}

/// <summary>The outcome of a region query (lower case on the wire).</summary>
public static class RegionQueryOutcomes
{
    /// <summary>The region was found and the answer holds its data.</summary>
    public const string Found = "found";
    /// <summary>No region with that id in the world (or, without a world, in any loaded world).</summary>
    public const string NotFound = "not-found";
    /// <summary>The game server failed to answer (see the message).</summary>
    public const string Error = "error";
}

/// <param name="QueryId">Unique per query (a GUID): the answer names it; a receiver answers once per id.</param>
/// <param name="RegionId">The WorldGuard region id.</param>
/// <param name="World">The world to look in, or null for the first loaded world that has the region.</param>
public sealed record RegionQueryPayload(string QueryId, string RegionId, string? World);

/// <summary>Anything a hub query waits for: the answer names its query.</summary>
public interface IRegionQueryAnswer
{
    string QueryId { get; }
}

/// <summary>The argument of <c>GameServerHub.RegionGeometryResult</c>.</summary>
public sealed class RegionGeometryResultDto : IRegionQueryAnswer
{
    public string QueryId { get; set; } = string.Empty;

    /// <summary>See <see cref="RegionQueryOutcomes"/>.</summary>
    public string Result { get; set; } = string.Empty;

    public string? Message { get; set; }

    /// <summary>The world the region was found in.</summary>
    public string? World { get; set; }

    /// <summary><c>cuboid</c>, <c>polygon</c> or another WorldGuard kind (lower case).</summary>
    public string? Shape { get; set; }

    public int MinX { get; set; }
    public int MinY { get; set; }
    public int MinZ { get; set; }
    public int MaxX { get; set; }
    public int MaxY { get; set; }
    public int MaxZ { get; set; }

    /// <summary>A polygon's points (x and z), in order; empty for a cuboid.</summary>
    public List<RegionPointDto>? Points { get; set; }
}

public sealed class RegionPointDto
{
    public int X { get; set; }
    public int Z { get; set; }
}

/// <summary>The argument of <c>GameServerHub.SpotSiteCheckResult</c> (knk-core <c>SpotSiteCheck.Result</c>).</summary>
public sealed class SpotSiteCheckResultDto : IRegionQueryAnswer
{
    public string QueryId { get; set; } = string.Empty;

    /// <summary>See <see cref="RegionQueryOutcomes"/>.</summary>
    public string Result { get; set; } = string.Empty;

    public string? Message { get; set; }

    /// <summary>Blocks in the layer under the spot.</summary>
    public int FloorBlocks { get; set; }

    /// <summary>Of those, the ones that aren't solid.</summary>
    public int OpenFloor { get; set; }

    /// <summary>False when the spot was too large to scan block by block.</summary>
    public bool VolumeChecked { get; set; }

    /// <summary>Blocks inside the spot that aren't clear.</summary>
    public long BlockedVolume { get; set; }

    /// <summary>One line per warning, empty when the site is fine.</summary>
    public List<string>? Warnings { get; set; }
}
