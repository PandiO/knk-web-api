using knkwebapi_v2.Dtos;
using knkwebapi_v2.Realtime.Contracts;
using knkwebapi_v2.Realtime.Presence;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Realtime;

/// <summary>
/// The hub-backed <see cref="IPlayerNotificationQueue"/> (DESIGN §4.3, PandiO/knk-workspace#179). The in-memory queue
/// stays the store and <c>GET api/PlayerNotifications/pending</c> / <c>POST acknowledge</c> work as before; on top of
/// it, each new notification is pushed at once (event <c>PlayerNotification</c>) when it can be shown now:
/// <list type="bullet">
///   <item>for the game server (user 0: staff alerts, lootbox world changes) → every connected game server;</item>
///   <item>for a player the presence registry has online → that player's server;</item>
///   <item>otherwise (no server connected, player offline) it only waits in the queue, as today.</item>
/// </list>
/// When a player joins over the hub, their waiting notifications (and the server-wide ones) are pushed to that
/// server. The plugin acknowledges over REST as before, so a push that got lost is still pending for the next
/// resync drain, and polls only while the hub is down.
/// </summary>
public sealed class HubPlayerNotificationQueue : IPlayerNotificationQueue
{
    private readonly InMemoryPlayerNotificationQueue _inner;
    private readonly IRealtimePublisher _publisher;
    private readonly RealtimeConnections _connections;
    private readonly PresenceRegistry _presence;

    public HubPlayerNotificationQueue(InMemoryPlayerNotificationQueue inner, IRealtimePublisher publisher,
        RealtimeConnections connections, PresenceRegistry presence)
    {
        _inner = inner;
        _publisher = publisher;
        _connections = connections;
        _presence = presence;
    }

    public long Enqueue(int userId, string? uuid, string username, string type, TitleChangeResultDto? titleChange) =>
        Pushed(_inner.Enqueue(userId, uuid, username, type, titleChange));

    public long EnqueuePayment(int userId, string? uuid, string username, PaymentNotificationDto payment) =>
        Pushed(_inner.EnqueuePayment(userId, uuid, username, payment));

    public long EnqueueCurrencyAlert(CurrencyAlertNotificationDto alert) =>
        Pushed(_inner.EnqueueCurrencyAlert(alert));

    public long EnqueueLootboxWorldChanged(LootboxWorldChangedNotificationDto change) =>
        Pushed(_inner.EnqueueLootboxWorldChanged(change));

    public long EnqueueDiscoveryReset(int userId, string? uuid, string username, DiscoveryResetNotificationDto reset) =>
        Pushed(_inner.EnqueueDiscoveryReset(userId, uuid, username, reset));

    public long EnqueueLocationOrphanDigest(LocationOrphanDigestNotificationDto digest) =>
        Pushed(_inner.EnqueueLocationOrphanDigest(digest));

    public long EnqueueAccountLinked(int userId, string? uuid, string username, AccountLinkedNotificationDto linked) =>
        Pushed(_inner.EnqueueAccountLinked(userId, uuid, username, linked));

    public IReadOnlyList<PlayerNotificationDto> GetPending() => _inner.GetPending();

    public int Acknowledge(IEnumerable<long> ids) => _inner.Acknowledge(ids);

    /// <summary>
    /// A player joined <paramref name="serverId"/> over the hub: pushes their waiting notifications and the
    /// server-wide ones, oldest first. Returns how many were pushed.
    /// </summary>
    public int PushPendingTo(string serverId, string uuid)
    {
        var normalized = PresenceRegistry.NormalizeUuid(uuid);
        if (normalized.Length == 0) return 0;
        var pushed = 0;
        foreach (var notification in _inner.GetPending())
        {
            if (notification.UserId == 0 || PresenceRegistry.NormalizeUuid(notification.Uuid) == normalized)
            {
                Publish(notification, serverId);
                pushed++;
            }
        }
        return pushed;
    }

    private long Pushed(long id)
    {
        if (_connections.Count(RealtimeHubKind.Game) == 0) return id;
        var notification = _inner.Find(id);
        if (notification == null) return id;
        if (notification.UserId == 0)
        {
            Publish(notification, null);
        }
        else if (!string.IsNullOrWhiteSpace(notification.Uuid) && _presence.ServerOf(notification.Uuid) is { } serverId)
        {
            Publish(notification, serverId);
        }
        return id;
    }

    private void Publish(PlayerNotificationDto notification, string? serverId) =>
        _publisher.PublishToGameServers(RealtimeEventTypes.PlayerNotification, notification, serverId, RealtimeActor.System);
}
