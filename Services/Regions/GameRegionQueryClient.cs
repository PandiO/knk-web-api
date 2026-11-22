using knkwebapi_v2.Configuration;
using knkwebapi_v2.Realtime;
using knkwebapi_v2.Realtime.Contracts;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Regions;

/// <summary>How a region query ended.</summary>
public enum RegionQueryStatus
{
    /// <summary>The game server found the region and answered.</summary>
    Found,
    /// <summary>The game server answered that there is no such region.</summary>
    NotFound,
    /// <summary>
    /// Nothing usable came back: <c>RegionQueries:Enabled</c> is off, no game server is connected, no answer in time
    /// (for the site check: also a game server with <c>siege.weapons.enabled</c> off), or the game server failed. A
    /// caller says it could not check and does not block a save.
    /// </summary>
    Unavailable,
}

/// <summary>A region's shape as the game server sees it. A polygon's min/max are its bounding box.</summary>
public sealed record RegionGeometry(string? World, string Shape, int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ,
    IReadOnlyList<(int X, int Z)> Points)
{
    public bool IsCuboid => string.Equals(Shape, "cuboid", StringComparison.OrdinalIgnoreCase);
}

public sealed record RegionGeometryAnswer(RegionQueryStatus Status, string Message, RegionGeometry? Geometry);

/// <summary>knk-core <c>SpotSiteCheck.Result</c> as answered.</summary>
public sealed record SpotSiteCheckAnswer(RegionQueryStatus Status, string Message, int FloorBlocks, int OpenFloor,
    bool VolumeChecked, long BlockedVolume, IReadOnlyList<string> Warnings)
{
    public bool HasWarnings => Status == RegionQueryStatus.Found && (OpenFloor > 0 || BlockedVolume > 0 || !VolumeChecked);
}

/// <summary>
/// PandiO/knk-workspace#988: asks a game server about a WorldGuard region over the game hub and waits for the answer.
/// Never throws for an unreachable server: the answer is <see cref="RegionQueryStatus.Unavailable"/> with the reason.
/// </summary>
public interface IGameRegionQueryClient
{
    /// <summary>The region's world, kind, min and max corner and points.</summary>
    Task<RegionGeometryAnswer> GetGeometryAsync(string regionId, string? world, CancellationToken ct = default);

    /// <summary>The siege weapon spot site check (floor solid, space clear) on the region's bounding box.</summary>
    Task<SpotSiteCheckAnswer> CheckSpotSiteAsync(string regionId, string? world, CancellationToken ct = default);
}

/// <summary>
/// Request and answer over SignalR, following gate commands (<see cref="Map.GateCommandService"/>): register the query id
/// with the waiter, publish the query to every connected game server, wait <c>RegionQueries:TimeoutSeconds</c> for the
/// first answer. The region validators that ask the plugin's HTTP server (<c>RegionService</c> → <c>RegionHttpServer</c>)
/// stay as they are; the hub needs no inbound port on the game server, and the answer comes back on the connection the
/// plugin already holds with the plugin key.
/// </summary>
public sealed class GameRegionQueryClient : IGameRegionQueryClient
{
    private readonly RegionQueryWaiter<RegionGeometryResultDto> _geometry;
    private readonly RegionQueryWaiter<SpotSiteCheckResultDto> _siteChecks;
    private readonly IOptionsMonitor<RegionQueryOptions> _options;
    private readonly Func<int> _connectedGameServers;
    private readonly IRealtimeEventSink? _realtime;
    private readonly TimeProvider _time;
    private readonly ILogger<GameRegionQueryClient>? _logger;

    public GameRegionQueryClient(RegionQueryWaiter<RegionGeometryResultDto> geometry,
        RegionQueryWaiter<SpotSiteCheckResultDto> siteChecks, IOptionsMonitor<RegionQueryOptions> options,
        RealtimeConnections connections, IRealtimeEventSink? realtime = null, TimeProvider? time = null,
        ILogger<GameRegionQueryClient>? logger = null)
        : this(geometry, siteChecks, options, () => connections.Count(RealtimeHubKind.Game), realtime, time, logger)
    {
    }

    /// <param name="connectedGameServers">How many game servers are connected to the hub now (tests).</param>
    public GameRegionQueryClient(RegionQueryWaiter<RegionGeometryResultDto> geometry,
        RegionQueryWaiter<SpotSiteCheckResultDto> siteChecks, IOptionsMonitor<RegionQueryOptions> options,
        Func<int> connectedGameServers, IRealtimeEventSink? realtime = null, TimeProvider? time = null,
        ILogger<GameRegionQueryClient>? logger = null)
    {
        _geometry = geometry;
        _siteChecks = siteChecks;
        _options = options;
        _connectedGameServers = connectedGameServers;
        _realtime = realtime;
        _time = time ?? TimeProvider.System;
        _logger = logger;
    }

    public async Task<RegionGeometryAnswer> GetGeometryAsync(string regionId, string? world, CancellationToken ct = default)
    {
        var (answer, problem) = await AskAsync(_geometry, RegionQueryEventTypes.RegionGeometryQuery, regionId, world,
            "the region's size", ct);
        if (answer == null) return new RegionGeometryAnswer(RegionQueryStatus.Unavailable, problem!, null);
        var status = Status(answer.Result);
        if (status != RegionQueryStatus.Found)
        {
            return new RegionGeometryAnswer(status, Trim(answer.Message) ?? DefaultMessage(status, regionId), null);
        }
        if (answer.MinX > answer.MaxX || answer.MinY > answer.MaxY || answer.MinZ > answer.MaxZ)
        {
            return new RegionGeometryAnswer(RegionQueryStatus.Unavailable,
                $"The game server's answer for region '{regionId}' has a min corner above its max corner.", null);
        }
        var points = (answer.Points ?? new List<RegionPointDto>()).Where(p => p != null).Take(1000)
            .Select(p => (p.X, p.Z)).ToList();
        var geometry = new RegionGeometry(Trim(answer.World) ?? world, (Trim(answer.Shape) ?? "cuboid").ToLowerInvariant(),
            answer.MinX, answer.MinY, answer.MinZ, answer.MaxX, answer.MaxY, answer.MaxZ, points);
        return new RegionGeometryAnswer(RegionQueryStatus.Found, Trim(answer.Message) ?? "Region found.", geometry);
    }

    public async Task<SpotSiteCheckAnswer> CheckSpotSiteAsync(string regionId, string? world, CancellationToken ct = default)
    {
        var (answer, problem) = await AskAsync(_siteChecks, RegionQueryEventTypes.SpotSiteCheckQuery, regionId, world,
            "the spot's floor and space", ct);
        if (answer == null)
        {
            return new SpotSiteCheckAnswer(RegionQueryStatus.Unavailable, problem!, 0, 0, false, 0, Array.Empty<string>());
        }
        var status = Status(answer.Result);
        var warnings = (answer.Warnings ?? new List<string>()).Where(w => !string.IsNullOrWhiteSpace(w)).Take(10)
            .Select(w => Trim(w)!).ToList();
        return new SpotSiteCheckAnswer(status, Trim(answer.Message) ?? DefaultMessage(status, regionId),
            Math.Max(0, answer.FloorBlocks), Math.Max(0, answer.OpenFloor), answer.VolumeChecked,
            Math.Max(0, answer.BlockedVolume), warnings);
    }

    /// <summary>The answer, or null and why there is none.</summary>
    private async Task<(TAnswer? Answer, string? Problem)> AskAsync<TAnswer>(RegionQueryWaiter<TAnswer> waiter, string type,
        string regionId, string? world, string what, CancellationToken ct) where TAnswer : class, IRegionQueryAnswer
    {
        var options = _options.CurrentValue;
        if (!options.Enabled)
            return (null, $"Region queries to the game server are switched off (RegionQueries:Enabled), so {what} wasn't checked.");
        if (string.IsNullOrWhiteSpace(regionId))
            return (null, "No region given.");
        if (_realtime == null || _connectedGameServers() <= 0)
            return (null, $"No game server is connected, so {what} wasn't checked.");

        var queryId = Guid.NewGuid().ToString();
        using var pending = waiter.Register(queryId);
        _realtime.Publish(new RealtimeEvent(type, new RegionQueryPayload(queryId, regionId.Trim(),
            string.IsNullOrWhiteSpace(world) ? null : world.Trim())));
        try
        {
            var answer = await pending.Result.WaitAsync(options.EffectiveTimeout, _time, ct);
            _logger?.LogDebug("Region query {QueryId} ({Type}) for {RegionId}: {Result}", queryId, type, regionId,
                Status(ResultOf(answer)));
            return (answer, null);
        }
        catch (TimeoutException)
        {
            _logger?.LogInformation("Region query {QueryId} ({Type}) for {RegionId}: no answer within {Timeout}s", queryId,
                type, regionId, options.EffectiveTimeout.TotalSeconds);
            return (null, $"No answer from the game server within {options.EffectiveTimeout.TotalSeconds:0} s, so {what} wasn't checked.");
        }
    }

    private static string? ResultOf(IRegionQueryAnswer answer) => answer switch
    {
        RegionGeometryResultDto g => g.Result,
        SpotSiteCheckResultDto s => s.Result,
        _ => null,
    };

    /// <summary>Anything other than found or not-found counts as not checked (fail open: the rules only warn).</summary>
    private static RegionQueryStatus Status(string? result) => result?.Trim().ToLowerInvariant() switch
    {
        RegionQueryOutcomes.Found => RegionQueryStatus.Found,
        RegionQueryOutcomes.NotFound => RegionQueryStatus.NotFound,
        _ => RegionQueryStatus.Unavailable,
    };

    private static string DefaultMessage(RegionQueryStatus status, string regionId) => status switch
    {
        RegionQueryStatus.NotFound => $"Region '{regionId}' doesn't exist on the game server.",
        RegionQueryStatus.Found => "Checked.",
        _ => "The game server couldn't answer.",
    };

    private static string? Trim(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Length <= 300 ? text.Trim() : text[..300].Trim();
}
