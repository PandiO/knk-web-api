using System.Threading.Channels;
using knkwebapi_v2.Realtime.Contracts;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Realtime;

/// <summary>
/// Publishes "this changed" notifications to the hubs (DESIGN §4.3, P1/P4/P5). Callers publish after their
/// <c>SaveChangesAsync</c> (and transaction) succeeded. Every method only queues and returns at once: a slow or
/// missing socket never slows a request, and nothing waits on the hub.
/// </summary>
public interface IRealtimePublisher
{
    /// <summary>To every connected game server, or only to <paramref name="serverId"/>.</summary>
    void PublishToGameServers(string type, object? payload, string? serverId = null, RealtimeActor? actor = null, int v = 1);

    /// <summary>To every web connection of <paramref name="userId"/>.</summary>
    void PublishToWebUser(int userId, string type, object? payload, RealtimeActor? actor = null, int v = 1);

    /// <summary>To one connection of either hub (e.g. the game server's <c>Welcome</c>).</summary>
    void PublishToConnection(RealtimeHubKind hub, string connectionId, string type, object? payload,
        RealtimeActor? actor = null, int v = 1);
}

/// <summary>A queued message: a group of one hub, or one connection.</summary>
public sealed record RealtimeMessage(RealtimeHubKind Hub, string? Group, string? ConnectionId, string Type, int V,
    object? Payload, RealtimeActor? Actor, DateTime At);

/// <summary>
/// <see cref="IRealtimePublisher"/> on a bounded channel (drop-oldest, logged). <see cref="RealtimeDispatchService"/>
/// reads it and sends.
/// </summary>
public sealed class RealtimePublisher : IRealtimePublisher
{
    private static readonly TimeSpan DropLogInterval = TimeSpan.FromSeconds(30);

    private readonly Channel<RealtimeMessage> _channel;
    private readonly ILogger<RealtimePublisher> _logger;
    private readonly RealtimeMetrics? _metrics;
    private readonly TimeProvider _time;
    private long _dropped;
    private long _lastDropLogTicks = long.MinValue;

    public RealtimePublisher(IOptions<RealtimeOptions> options, ILogger<RealtimePublisher> logger,
        RealtimeMetrics? metrics = null, TimeProvider? time = null)
    {
        _logger = logger;
        _metrics = metrics;
        _time = time ?? TimeProvider.System;
        var capacity = Math.Max(1, options.Value.ChannelCapacity);
        _channel = Channel.CreateBounded<RealtimeMessage>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            },
            OnDropped);
    }

    public ChannelReader<RealtimeMessage> Reader => _channel.Reader;

    /// <summary>Messages dropped since start because the queue was full.</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    public void PublishToGameServers(string type, object? payload, string? serverId = null, RealtimeActor? actor = null, int v = 1) =>
        Enqueue(RealtimeHubKind.Game, serverId == null ? RealtimeGroups.Servers : RealtimeGroups.Server(serverId), null,
            type, payload, actor, v);

    public void PublishToWebUser(int userId, string type, object? payload, RealtimeActor? actor = null, int v = 1) =>
        Enqueue(RealtimeHubKind.Web, RealtimeGroups.User(userId), null, type, payload, actor, v);

    public void PublishToConnection(RealtimeHubKind hub, string connectionId, string type, object? payload,
        RealtimeActor? actor = null, int v = 1) =>
        Enqueue(hub, null, connectionId, type, payload, actor, v);

    /// <summary>Stops accepting messages (shutdown); the dispatcher drains what is queued.</summary>
    public void Complete() => _channel.Writer.TryComplete();

    private void Enqueue(RealtimeHubKind hub, string? group, string? connectionId, string type, object? payload,
        RealtimeActor? actor, int v)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("An event type is required.", nameof(type));
        }
        var message = new RealtimeMessage(hub, group, connectionId, type, v, payload, actor, _time.GetUtcNow().UtcDateTime);
        // Bounded + DropOldest: TryWrite only fails once the channel is completed (shutdown).
        if (_channel.Writer.TryWrite(message))
        {
            _metrics?.Published(hub, type);
        }
    }

    private void OnDropped(RealtimeMessage dropped)
    {
        var total = Interlocked.Increment(ref _dropped);
        _metrics?.Dropped(dropped.Hub, dropped.Type);
        var now = _time.GetUtcNow().UtcTicks;
        var last = Interlocked.Read(ref _lastDropLogTicks);
        if (last == long.MinValue || now - last >= DropLogInterval.Ticks)
        {
            if (Interlocked.CompareExchange(ref _lastDropLogTicks, now, last) == last)
            {
                _logger.LogWarning(
                    "Realtime queue full: dropped the oldest message ({Type} to {Hub}); {Total} dropped since start. Receivers catch up on their next resync.",
                    dropped.Type, dropped.Hub, total);
            }
        }
    }
}
