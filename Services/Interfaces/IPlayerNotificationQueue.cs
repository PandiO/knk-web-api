using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces;

/// <summary>
/// Hand-off from the web API to the Minecraft plugin for in-game moments (promotion effects)
/// triggered by a write the plugin didn't make itself - e.g. an XP grant from the web admin's
/// PlayerProfilePage. The plugin polls GetPending, shows each notification whose player is
/// online, then acknowledges it; notifications for offline players wait until they join or
/// expire. Designed to be extensible: in-memory now (same precedent as IClientActivityStore),
/// a persisted table or push channel later. Losing the queue on an API restart only loses the
/// cosmetic moment - the balances and rewards themselves are already persisted.
/// </summary>
public interface IPlayerNotificationQueue
{
    /// <returns>The new notification's id.</returns>
    long Enqueue(int userId, string? uuid, string username, string type, TitleChangeResultDto? titleChange);

    /// <summary>A PaymentReceived notification carrying <paramref name="payment"/> (currency Phase 3).
    /// The default only queues the type, for implementations that don't carry payloads.</summary>
    /// <returns>The new notification's id.</returns>
    long EnqueuePayment(int userId, string? uuid, string username, PaymentNotificationDto payment) =>
        Enqueue(userId, uuid, username, PlayerNotificationTypes.PaymentReceived, null);

    /// <summary>A CurrencyAlert notification for online staff (currency Phase 5): UserId 0, no
    /// player. The default only queues the type.</summary>
    /// <returns>The new notification's id.</returns>
    long EnqueueCurrencyAlert(CurrencyAlertNotificationDto alert) =>
        Enqueue(0, null, "", PlayerNotificationTypes.CurrencyAlert, null);

    /// <summary>A server-wide lootbox change (UserId 0), see PlayerNotificationTypes.LootboxWorldChanged. The
    /// default only queues the type.</summary>
    long EnqueueLootboxWorldChanged(LootboxWorldChangedNotificationDto change) =>
        Enqueue(0, null, "", PlayerNotificationTypes.LootboxWorldChanged, null);

    /// <summary>A DiscoveryReset notification (domain discovery): one of the user's discoveries
    /// was reset, so the plugin re-syncs its known set. The default only queues the type.</summary>
    /// <returns>The new notification's id.</returns>
    long EnqueueDiscoveryReset(int userId, string? uuid, string username, DiscoveryResetNotificationDto reset) =>
        Enqueue(userId, uuid, username, PlayerNotificationTypes.DiscoveryReset, null);

    /// <summary>A Location orphan digest for online staff (KNG-80): UserId 0, no player. The default
    /// only queues the type.</summary>
    /// <returns>The new notification's id.</returns>
    long EnqueueLocationOrphanDigest(LocationOrphanDigestNotificationDto digest) =>
        Enqueue(0, null, "", PlayerNotificationTypes.LocationOrphanDigest, null);

    /// <summary>Every unacknowledged, unexpired notification, oldest first.</summary>
    IReadOnlyList<PlayerNotificationDto> GetPending();

    /// <returns>How many of the given ids were still pending and are now removed.</returns>
    int Acknowledge(IEnumerable<long> ids);
}
