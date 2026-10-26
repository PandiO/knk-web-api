using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Realtime;
using knkwebapi_v2.Realtime.Contracts;
using knkwebapi_v2.Realtime.Presence;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Realtime;

/// <summary>
/// PandiO/knk-workspace#179 (DESIGN §4.3): the hub-backed player notification queue pushes what can be shown now and
/// keeps everything in the in-memory queue the REST poll reads.
/// </summary>
public class HubPlayerNotificationQueueTests
{
    private const string Alice = "00000000-0000-0000-0000-0000000000a1";
    private const string Bob = "00000000-0000-0000-0000-0000000000b2";

    private sealed class RecordingPublisher : IRealtimePublisher
    {
        public List<(string Type, PlayerNotificationDto Notification, string? ServerId)> Sent { get; } = new();

        public void PublishToGameServers(string type, object? payload, string? serverId = null, RealtimeActor? actor = null, int v = 1) =>
            Sent.Add((type, (PlayerNotificationDto)payload!, serverId));

        public void PublishToWebUser(int userId, string type, object? payload, RealtimeActor? actor = null, int v = 1) { }

        public void PublishToConnection(RealtimeHubKind hub, string connectionId, string type, object? payload,
            RealtimeActor? actor = null, int v = 1) { }
    }

    private readonly InMemoryPlayerNotificationQueue _inner = new();
    private readonly RecordingPublisher _publisher = new();
    private readonly RealtimeConnections _connections = new();
    private readonly PresenceRegistry _presence = new();
    private readonly HubPlayerNotificationQueue _queue;

    public HubPlayerNotificationQueueTests()
    {
        _queue = new HubPlayerNotificationQueue(_inner, _publisher, _connections, _presence);
    }

    private void ServerConnected(string serverId, params string[] online)
    {
        _connections.Add(new RealtimeConnection("c-" + serverId, RealtimeHubKind.Game,
            new[] { RealtimeGroups.Servers, RealtimeGroups.Server(serverId) }, serverId));
        _presence.ServerConnected(serverId, "c-" + serverId);
        _presence.ReplaceRoster(serverId, online.Select(u => new RosterEntry(u, null, null, null)));
    }

    [Fact]
    public void NoServerConnected_OnlyQueues_AsBefore()
    {
        var id = _queue.Enqueue(1, Alice, "alice", PlayerNotificationTypes.RankChanged, null);

        Assert.Empty(_publisher.Sent);
        Assert.Equal(id, Assert.Single(_queue.GetPending()).Id);
    }

    [Fact]
    public void PlayerOnline_IsPushedToTheirServer_AndStaysPendingUntilAcknowledged()
    {
        ServerConnected("survival", Alice);

        var id = _queue.EnqueueDiscoveryReset(1, Alice.ToUpperInvariant(), "alice", new DiscoveryResetNotificationDto());

        var sent = Assert.Single(_publisher.Sent);
        Assert.Equal(RealtimeEventTypes.PlayerNotification, sent.Type);
        Assert.Equal("survival", sent.ServerId);
        Assert.Equal(id, sent.Notification.Id);
        Assert.Equal(PlayerNotificationTypes.DiscoveryReset, sent.Notification.Type);
        Assert.Single(_queue.GetPending());
        Assert.Equal(1, _queue.Acknowledge(new[] { id }));
        Assert.Empty(_queue.GetPending());
    }

    [Fact]
    public void PlayerOffline_WaitsInTheQueue_AndIsPushedWhenTheyJoin()
    {
        ServerConnected("survival", Bob);
        var id = _queue.EnqueuePayment(1, Alice, "alice", new PaymentNotificationDto());
        Assert.Empty(_publisher.Sent);

        Assert.Equal(1, _queue.PushPendingTo("survival", Alice));

        var sent = Assert.Single(_publisher.Sent);
        Assert.Equal(id, sent.Notification.Id);
        Assert.Equal("survival", sent.ServerId);
    }

    [Fact]
    public void ServerWideNotifications_GoToEveryServer_AndAgainOnAJoin()
    {
        ServerConnected("survival");
        _queue.EnqueueCurrencyAlert(new CurrencyAlertNotificationDto());
        _queue.Enqueue(2, Bob, "bob", PlayerNotificationTypes.RankChanged, null);

        var alert = Assert.Single(_publisher.Sent);
        Assert.Null(alert.ServerId);
        Assert.Equal(0, alert.Notification.UserId);

        // A join pushes the server-wide one again (nobody may have been online to see it) and the player's own.
        Assert.Equal(1, _queue.PushPendingTo("survival", Alice));
        Assert.Equal(2, _queue.PushPendingTo("survival", Bob));
    }

    [Fact]
    public void PushPendingTo_WithoutAUuid_PushesNothing()
    {
        ServerConnected("survival");
        _queue.EnqueueCurrencyAlert(new CurrencyAlertNotificationDto());
        _publisher.Sent.Clear();

        Assert.Equal(0, _queue.PushPendingTo("survival", " "));
        Assert.Empty(_publisher.Sent);
    }
}
