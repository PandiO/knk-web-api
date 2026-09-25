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

    /// <summary>Every unacknowledged, unexpired notification, oldest first.</summary>
    IReadOnlyList<PlayerNotificationDto> GetPending();

    /// <returns>How many of the given ids were still pending and are now removed.</returns>
    int Acknowledge(IEnumerable<long> ids);
}
