using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces;

/// <summary>Server scans of the Minecraft registry and their ingest (KNG-200 phase 3, plan §5.2a and §6).</summary>
public interface IMinecraftCatalogScanService
{
    /// <summary>Starts a scan: a WorldTask the plugin picks up.</summary>
    /// <exception cref="knkwebapi_v2.Services.MinecraftCatalog.MinecraftCatalogConflictException">A scan is already active.</exception>
    Task<MinecraftCatalogScanDto> RequestScanAsync(int requestedByUserId, CancellationToken ct = default);

    Task<IReadOnlyList<MinecraftCatalogScanDto>> ListAsync(int take, CancellationToken ct = default);

    /// <summary>The scan, synced with its WorldTask (a failed task fails the scan); null when unknown.</summary>
    Task<MinecraftCatalogScanDto?> GetAsync(int scanId, CancellationToken ct = default);

    /// <exception cref="KeyNotFoundException">Unknown scan.</exception>
    /// <exception cref="knkwebapi_v2.Services.MinecraftCatalog.MinecraftCatalogConflictException">The scan is no longer active.</exception>
    Task<MinecraftCatalogScanDto> CancelAsync(int scanId, CancellationToken ct = default);

    /// <summary>Validates and applies a snapshot in one transaction: upsert, clean-up (D7), icons.</summary>
    /// <exception cref="ArgumentException">Invalid snapshot; nothing is written.</exception>
    /// <exception cref="KeyNotFoundException">Unknown scan.</exception>
    /// <exception cref="knkwebapi_v2.Services.MinecraftCatalog.MinecraftCatalogConflictException">The scan can't take this snapshot.</exception>
    Task<MinecraftCatalogScanDto> IngestAsync(int scanId, Dtos.MinecraftCatalogSnapshotDto snapshot, CancellationToken ct = default);

    /// <summary>Sets or clears (null/blank) a material's manual icon.</summary>
    /// <exception cref="KeyNotFoundException">Unknown material.</exception>
    /// <exception cref="ArgumentException">Not an http(s) URL.</exception>
    Task SetIconOverrideAsync(int materialId, string? iconUrlOverride, CancellationToken ct = default);
}
