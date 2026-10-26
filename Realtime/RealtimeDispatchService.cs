using knkwebapi_v2.Realtime.Contracts;
using knkwebapi_v2.Realtime.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace knkwebapi_v2.Realtime;

/// <summary>Sends one envelope to one connection. Abstracted so the dispatcher is testable without a hub.</summary>
public interface IRealtimeTransport
{
    Task SendAsync(RealtimeHubKind hub, string connectionId, RealtimeEnvelope envelope, CancellationToken cancellationToken);
}

/// <summary>Sends through <see cref="IHubContext{THub}"/>: the hub method name is the event type.</summary>
public sealed class HubContextRealtimeTransport : IRealtimeTransport
{
    private readonly IHubContext<GameServerHub> _game;
    private readonly IHubContext<WebHub> _web;

    public HubContextRealtimeTransport(IHubContext<GameServerHub> game, IHubContext<WebHub> web)
    {
        _game = game;
        _web = web;
    }

    public Task SendAsync(RealtimeHubKind hub, string connectionId, RealtimeEnvelope envelope, CancellationToken cancellationToken)
    {
        var clients = hub == RealtimeHubKind.Game ? _game.Clients : _web.Clients;
        return clients.Client(connectionId).SendAsync(envelope.Type, envelope, cancellationToken);
    }
}

/// <summary>
/// Reads the publisher's queue and sends each message to every connection of its group, numbering it per connection
/// (DESIGN §4.2). One reader, so the order per connection is the publish order. A failed send is logged and
/// skipped; the receiver sees the gap in <c>seq</c> and resyncs (P2).
/// </summary>
public sealed class RealtimeDispatchService : BackgroundService
{
    private readonly RealtimePublisher _publisher;
    private readonly RealtimeConnections _connections;
    private readonly IRealtimeTransport _transport;
    private readonly ILogger<RealtimeDispatchService> _logger;
    private readonly RealtimeMetrics? _metrics;

    public RealtimeDispatchService(RealtimePublisher publisher, RealtimeConnections connections, IRealtimeTransport transport,
        ILogger<RealtimeDispatchService> logger, RealtimeMetrics? metrics = null)
    {
        _publisher = publisher;
        _connections = connections;
        _transport = transport;
        _logger = logger;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in _publisher.Reader.ReadAllAsync(stoppingToken))
            {
                await DispatchAsync(message, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Sends one message to its connections; returns how many sends succeeded.</summary>
    public async Task<int> DispatchAsync(RealtimeMessage message, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RealtimeConnection> targets;
        if (message.ConnectionId != null)
        {
            var one = _connections.Get(message.ConnectionId);
            targets = one == null || one.Hub != message.Hub ? Array.Empty<RealtimeConnection>() : new[] { one };
        }
        else
        {
            targets = _connections.InGroup(message.Hub, message.Group ?? string.Empty);
        }

        var sent = 0;
        foreach (var connection in targets)
        {
            var envelope = new RealtimeEnvelope(message.V, connection.NextSeq(), message.Type, message.At, message.Actor, message.Payload);
            try
            {
                await _transport.SendAsync(message.Hub, connection.ConnectionId, envelope, cancellationToken);
                sent++;
                _metrics?.Sent(message.Hub, message.Type);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _metrics?.Failed(message.Hub, message.Type);
                _logger.LogWarning(ex, "Realtime send of {Type} (seq {Seq}) to {Hub} connection {ConnectionId} failed",
                    message.Type, envelope.Seq, message.Hub, connection.ConnectionId);
            }
        }
        return sent;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _publisher.Complete();
        return base.StopAsync(cancellationToken);
    }
}
