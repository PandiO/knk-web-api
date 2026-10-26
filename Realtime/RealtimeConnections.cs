using System.Collections.Concurrent;

namespace knkwebapi_v2.Realtime;

public enum RealtimeHubKind
{
    /// <summary><c>/api/hubs/game</c>: game servers (plugin key + server id).</summary>
    Game,

    /// <summary><c>/api/hubs/web</c>: logged-in web users (JWT).</summary>
    Web
}

/// <summary>Logical groups (DESIGN §4.1). Membership is decided by the API only; clients can't join a group.</summary>
public static class RealtimeGroups
{
    /// <summary>Every connected game server.</summary>
    public const string Servers = "servers";

    public static string Server(string serverId) => "server:" + serverId;

    public static string User(int userId) => "user:" + userId;
}

/// <summary>One open hub connection. <see cref="NextSeq"/> numbers the messages sent to it (DESIGN §4.2).</summary>
public sealed class RealtimeConnection
{
    private long _seq;

    public RealtimeConnection(string connectionId, RealtimeHubKind hub, IEnumerable<string> groups,
        string? serverId = null, int? userId = null)
    {
        ConnectionId = connectionId;
        Hub = hub;
        Groups = new HashSet<string>(groups, StringComparer.Ordinal);
        ServerId = serverId;
        UserId = userId;
    }

    public string ConnectionId { get; }
    public RealtimeHubKind Hub { get; }
    public IReadOnlySet<string> Groups { get; }
    public string? ServerId { get; }
    public int? UserId { get; }

    /// <summary>Messages numbered so far.</summary>
    public long LastSeq => Interlocked.Read(ref _seq);

    public long NextSeq() => Interlocked.Increment(ref _seq);
}

/// <summary>
/// The open connections of both hubs and their groups. SignalR's own groups can't number messages per connection,
/// so the publisher expands a group to its connections here and sends to each one with its own <c>seq</c>.
/// </summary>
public sealed class RealtimeConnections
{
    private readonly ConcurrentDictionary<string, RealtimeConnection> _byId = new(StringComparer.Ordinal);

    public void Add(RealtimeConnection connection) => _byId[connection.ConnectionId] = connection;

    public RealtimeConnection? Remove(string connectionId) =>
        _byId.TryRemove(connectionId, out var removed) ? removed : null;

    public RealtimeConnection? Get(string connectionId) =>
        _byId.TryGetValue(connectionId, out var connection) ? connection : null;

    public IReadOnlyList<RealtimeConnection> InGroup(RealtimeHubKind hub, string group) =>
        _byId.Values.Where(c => c.Hub == hub && c.Groups.Contains(group)).ToList();

    public int Count(RealtimeHubKind hub) => _byId.Values.Count(c => c.Hub == hub);
}
