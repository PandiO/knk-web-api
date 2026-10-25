using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces;

/// <summary>Backfill, audit and merge for the Minecraft catalogue (KNG-200 phase 2, plan §8).</summary>
public interface IMinecraftCatalogMaintenanceService
{
    /// <summary>Normalises keys, fills display names and links block refs to their material. Idempotent.</summary>
    Task<MinecraftCatalogBackfillResultDto> BackfillAsync(CancellationToken ct = default);

    /// <summary>Read-only report of collisions, duplicates and unlinked rows.</summary>
    Task<MinecraftCatalogAuditDto> AuditAsync(CancellationToken ct = default);

    /// <summary>Points every reference to <paramref name="sourceId"/> at <paramref name="targetId"/>, then deletes the source.</summary>
    /// <exception cref="KeyNotFoundException">A material doesn't exist.</exception>
    /// <exception cref="ArgumentException">Source and target are the same.</exception>
    Task<MinecraftMaterialMergeResultDto> MergeAsync(int sourceId, int targetId, CancellationToken ct = default);
}
