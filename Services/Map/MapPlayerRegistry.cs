using knkwebapi_v2.Realtime.Contracts;
using knkwebapi_v2.Realtime.Presence;

namespace knkwebapi_v2.Services.Map;

/// <summary>A player's active mode as the map sees it (O07, O22).</summary>
public enum MapPlayerMode
{
    None,
    Staff,
    Owner,

    /// <summary>A mode value the API does not know: treated as hidden, like staff and owner mode (fail closed).</summary>
    Unknown,
}

/// <summary>The latest report of one online player (spec § 5 rule 6).</summary>
public sealed record MapPlayerEntry(
    string ServerId,
    string Uuid,
    string World,
    double X,
    double Y,
    double Z,
    int Yaw,
    bool Afk,
    MapPlayerMode Mode,
    string? Minigame,
    long? MatchId,
    DateTime ReceivedAt,
    int IntervalMs)
{
    /// <summary>Hidden from the anonymous and logged-in snapshots: in a mode (O07, O22) or a running match (O13).</summary>
    public bool HiddenFromPublic => Mode != MapPlayerMode.None || Minigame != null;
}

/// <summary>
/// Overview map L4 (PandiO/knk-workspace#738, OVERVIEW_MAP_DESIGN § 6.1.3, § 5 rule 6): the latest report per online
/// player from the game hub's <c>ReportMapPlayers</c>, beside <see cref="PresenceRegistry"/>. In memory only, empty on
/// API start. A player is dropped on <c>PlayerLeft</c> or once their entry is older than three report intervals (the
/// game server stopped reporting them). Thread-safe; UUIDs lower case. Never served directly: the per-audience
/// snapshots (<see cref="MapPlayerSnapshots"/>) decide what each viewer receives.
/// </summary>
public sealed class MapPlayerRegistry
{
    /// <summary>Bounds of a report's interval: the plugin clamps to 10..1200 ticks (0.5..60 s).</summary>
    public const int MinIntervalMs = 500;
    public const int MaxIntervalMs = 60_000;

    /// <summary>A player is dropped after this many missed intervals (spec § 5 rule 6).</summary>
    public const int MissedIntervals = 3;

    private const int MaxWorldLength = 64;
    private const int MaxMinigameLength = 32;

    private readonly object _lock = new();
    private readonly Dictionary<string, MapPlayerEntry> _players = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public MapPlayerRegistry(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    /// <summary>Stores every valid player of one report message; returns how many were stored.</summary>
    public int Apply(string serverId, MapPlayersReportDto? report)
    {
        if (report?.Players == null || report.Players.Count == 0) return 0;
        var interval = Math.Clamp(report.IntervalMs <= 0 ? 2000 : report.IntervalMs, MinIntervalMs, MaxIntervalMs);
        var now = Now;
        var stored = 0;
        lock (_lock)
        {
            foreach (var player in report.Players)
            {
                var entry = ToEntry(serverId, player, now, interval);
                if (entry == null) continue;
                _players[entry.Uuid] = entry;
                stored++;
            }
        }
        return stored;
    }

    /// <summary>The player left their server (<c>PlayerLeft</c>): dropped at once.</summary>
    public bool Remove(string? uuid)
    {
        var normalized = PresenceRegistry.NormalizeUuid(uuid);
        if (normalized.Length == 0) return false;
        lock (_lock)
        {
            return _players.Remove(normalized);
        }
    }

    /// <summary>Every player reported within the last three intervals; older entries are dropped.</summary>
    public IReadOnlyList<MapPlayerEntry> Live()
    {
        var now = Now;
        lock (_lock)
        {
            var stale = _players.Values
                .Where(p => now - p.ReceivedAt > TimeSpan.FromMilliseconds((double)p.IntervalMs * MissedIntervals))
                .Select(p => p.Uuid)
                .ToList();
            foreach (var uuid in stale)
            {
                _players.Remove(uuid);
            }
            return _players.Values.ToList();
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _players.Count;
            }
        }
    }

    private static MapPlayerEntry? ToEntry(string serverId, MapPlayerReportDto? p, DateTime now, int interval)
    {
        if (p == null || !Guid.TryParse(p.Uuid, out var guid)) return null;
        var world = p.World?.Trim();
        if (string.IsNullOrEmpty(world) || world.Length > MaxWorldLength) return null;
        if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z) || !double.IsFinite(p.Yaw)) return null;
        var minigame = p.Minigame?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(minigame)) minigame = null;
        else if (minigame.Length > MaxMinigameLength) minigame = minigame[..MaxMinigameLength];
        var yaw = (int)Math.Round(p.Yaw);
        return new MapPlayerEntry(serverId, guid.ToString("D"), world, p.X, p.Y, p.Z, yaw, p.Afk, ParseMode(p.Mode),
            minigame, minigame == null ? null : p.MatchId, now, interval);
    }

    public static MapPlayerMode ParseMode(string? mode) => (mode ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "" or "none" => MapPlayerMode.None,
        "staff" => MapPlayerMode.Staff,
        "owner" => MapPlayerMode.Owner,
        _ => MapPlayerMode.Unknown,
    };
}
