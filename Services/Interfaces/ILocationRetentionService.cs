using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces;

/// <summary>
/// Location retention (KNG-80): finds orphaned Locations (default name, nothing references them,
/// older than the grace period), keeps them as review items, and lets staff keep or delete each
/// one. Never deletes anything by itself.
/// </summary>
public interface ILocationRetentionService
{
    /// <summary>Runs the orphan check now. Null when a run is already going on.</summary>
    Task<LocationRetentionRunDto?> RunCheckAsync(string trigger, int? triggeredByUserId, DateTime? scheduledSlotUtc = null, CancellationToken ct = default);

    /// <summary>The schedule slot (UTC) a scheduled run is due for at <paramref name="nowUtc"/>, or null.</summary>
    Task<DateTime?> DueScheduledSlotAsync(DateTime nowUtc, CancellationToken ct = default);

    /// <param name="status">open (default), kept, deleted, resolved or all</param>
    Task<LocationOrphanPageDto> ListAsync(string status, int page, int pageSize, CancellationToken ct = default);

    /// <exception cref="KeyNotFoundException">No such item.</exception>
    /// <exception cref="InvalidOperationException">The item isn't open.</exception>
    Task<LocationOrphanDto> KeepAsync(int itemId, int actorUserId, string? note, CancellationToken ct = default);

    /// <summary>
    /// Deletes the Location after re-checking, in the same transaction, that it is still an orphan.
    /// When it isn't (a relation or custom name appeared), nothing is deleted and the item is
    /// marked Resolved.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such item.</exception>
    /// <exception cref="InvalidOperationException">The item isn't open.</exception>
    Task<LocationOrphanDeleteResultDto> DeleteAsync(int itemId, int actorUserId, string? note, CancellationToken ct = default);

    Task<LocationRetentionStatusDto> GetStatusAsync(CancellationToken ct = default);

    /// <exception cref="ArgumentException">An invalid value.</exception>
    Task<LocationRetentionSettingsDto> UpdateSettingsAsync(LocationRetentionSettingsDto settings, int? actorUserId, CancellationToken ct = default);

    /// <summary>Any Location's coordinates, for /knk location tp. Null when it doesn't exist.</summary>
    Task<LocationTeleportTargetDto?> GetTeleportTargetAsync(int locationId, CancellationToken ct = default);
}
