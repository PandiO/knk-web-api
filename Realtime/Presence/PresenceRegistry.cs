namespace knkwebapi_v2.Realtime.Presence;

/// <summary>One player the registry holds as online on a game server.</summary>
public sealed record PresentPlayer(string Uuid, string? Name, string? World, int? UserId, DateTime JoinedAt);

/// <summary>A game server whose connection dropped and did not come back within the grace period.</summary>
public sealed record ExpiredServer(string ServerId, IReadOnlyCollection<string> OfflineUuids);

/// <summary>What a roster changed: who is online now, and who left this server and is online nowhere else.</summary>
public sealed record RosterChange(IReadOnlyCollection<string> Online, IReadOnlyCollection<string> GoneOffline);

/// <summary>
/// Who is online where, from the game hub (DESIGN §5): <c>serverId → {connection, players}</c>. In memory only; empty
/// on API start, filled by each server's first roster. A server that disconnects keeps its players for the grace
/// period (it usually reconnects with a fresh roster); after that they count as offline. Thread-safe.
/// UUIDs are kept in lower case.
/// </summary>
public sealed class PresenceRegistry
{
    private sealed class ServerEntry
    {
        public ServerEntry(string serverId) => ServerId = serverId;

        public string ServerId { get; }
        public string? ConnectionId { get; set; }
        public DateTime ConnectedAt { get; set; }
        public DateTime? DisconnectedAt { get; set; }
        public Dictionary<string, PresentPlayer> Players { get; } = new(StringComparer.Ordinal);
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, ServerEntry> _servers = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public PresenceRegistry(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    public static string NormalizeUuid(string? uuid) => (uuid ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// A connection for <paramref name="serverId"/> opened. Returns true when the server was known (a reconnect, or
    /// a second connection replacing a stale one); its players stay until the roster replaces them.
    /// </summary>
    public bool ServerConnected(string serverId, string connectionId)
    {
        lock (_lock)
        {
            var known = _servers.TryGetValue(serverId, out var entry);
            if (entry == null)
            {
                entry = new ServerEntry(serverId);
                _servers[serverId] = entry;
            }
            entry.ConnectionId = connectionId;
            entry.ConnectedAt = Now;
            entry.DisconnectedAt = null;
            return known;
        }
    }

    /// <summary>
    /// The connection dropped. Ignored when the server already reconnected on another connection. Starts the grace
    /// period.
    /// </summary>
    public void ServerDisconnected(string serverId, string connectionId)
    {
        lock (_lock)
        {
            if (_servers.TryGetValue(serverId, out var entry) && entry.ConnectionId == connectionId)
            {
                entry.ConnectionId = null;
                entry.DisconnectedAt = Now;
            }
        }
    }

    /// <summary>Replaces <paramref name="serverId"/>'s players with its roster.</summary>
    public RosterChange ReplaceRoster(string serverId, IEnumerable<RosterEntry> roster)
    {
        lock (_lock)
        {
            if (!_servers.TryGetValue(serverId, out var entry))
            {
                entry = new ServerEntry(serverId) { ConnectedAt = Now };
                _servers[serverId] = entry;
            }
            var before = entry.Players.Keys.ToHashSet(StringComparer.Ordinal);
            entry.Players.Clear();
            foreach (var player in roster)
            {
                var uuid = NormalizeUuid(player.Uuid);
                if (uuid.Length == 0) continue;
                var joinedAt = player.JoinedAt ?? Now;
                entry.Players[uuid] = new PresentPlayer(uuid, player.Name, player.World, player.UserId, joinedAt);
            }
            var online = entry.Players.Keys.ToList();
            var goneOffline = before.Where(u => !entry.Players.ContainsKey(u) && !OnlineElsewhere(u, serverId)).ToList();
            return new RosterChange(online, goneOffline);
        }
    }

    /// <summary>A player joined <paramref name="serverId"/>. Returns the normalized UUID (empty when invalid).</summary>
    public string PlayerJoined(string serverId, RosterEntry player)
    {
        var uuid = NormalizeUuid(player.Uuid);
        if (uuid.Length == 0) return uuid;
        lock (_lock)
        {
            if (!_servers.TryGetValue(serverId, out var entry))
            {
                entry = new ServerEntry(serverId) { ConnectedAt = Now };
                _servers[serverId] = entry;
            }
            entry.Players[uuid] = new PresentPlayer(uuid, player.Name, player.World, player.UserId, Now);
        }
        return uuid;
    }

    /// <summary>A player left <paramref name="serverId"/>. Returns true when they are now online nowhere.</summary>
    public bool PlayerLeft(string serverId, string uuid)
    {
        var normalized = NormalizeUuid(uuid);
        if (normalized.Length == 0) return false;
        lock (_lock)
        {
            if (_servers.TryGetValue(serverId, out var entry))
            {
                entry.Players.Remove(normalized);
            }
            return !OnlineElsewhere(normalized, null);
        }
    }

    /// <summary>
    /// Drops every server disconnected for longer than <paramref name="grace"/> and returns, per server, the players
    /// that are now online nowhere.
    /// </summary>
    public IReadOnlyList<ExpiredServer> ExpireDisconnected(TimeSpan grace)
    {
        var now = Now;
        lock (_lock)
        {
            var expired = _servers.Values
                .Where(s => s.ConnectionId == null && s.DisconnectedAt.HasValue && now - s.DisconnectedAt.Value >= grace)
                .ToList();
            var result = new List<ExpiredServer>();
            foreach (var server in expired)
            {
                _servers.Remove(server.ServerId);
            }
            foreach (var server in expired)
            {
                var offline = server.Players.Keys.Where(u => !OnlineElsewhere(u, server.ServerId)).ToList();
                result.Add(new ExpiredServer(server.ServerId, offline));
            }
            return result;
        }
    }

    /// <summary>The server <paramref name="uuid"/> is online on, or null (DESIGN §5: "is the initiator online?").</summary>
    public string? ServerOf(string uuid)
    {
        var normalized = NormalizeUuid(uuid);
        lock (_lock)
        {
            return _servers.Values.FirstOrDefault(s => s.Players.ContainsKey(normalized))?.ServerId;
        }
    }

    /// <summary>Every UUID online on any known server (connected or within its grace period).</summary>
    public IReadOnlySet<string> AllOnlineUuids()
    {
        lock (_lock)
        {
            return _servers.Values.SelectMany(s => s.Players.Keys).ToHashSet(StringComparer.Ordinal);
        }
    }

    /// <summary>Known servers and whether they are connected (diagnostics).</summary>
    public IReadOnlyList<(string ServerId, bool Connected, int Players)> Servers()
    {
        lock (_lock)
        {
            return _servers.Values.Select(s => (s.ServerId, s.ConnectionId != null, s.Players.Count)).ToList();
        }
    }

    private bool OnlineElsewhere(string uuid, string? exceptServerId) =>
        _servers.Values.Any(s => s.ServerId != exceptServerId && s.Players.ContainsKey(uuid));
}

/// <summary>Input for <see cref="PresenceRegistry"/>, from the hub DTOs.</summary>
public sealed record RosterEntry(string Uuid, string? Name, string? World, int? UserId, DateTime? JoinedAt = null);
