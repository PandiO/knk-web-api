using System.Text.Json;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.MinecraftCatalog;

/// <summary>The request can't be applied in the scan's current state (HTTP 409).</summary>
public class MinecraftCatalogConflictException : InvalidOperationException
{
    public MinecraftCatalogConflictException(string message) : base(message)
    {
    }
}

/// <summary>
/// KNG-200 phase 3 (docs/specs/minecraft-catalog/IMPLEMENTATION_PLAN.md §5.2a, §6): a scan is a
/// headless WorldTask for knk-plugin; the plugin's snapshot is validated in full, icons are resolved,
/// and then one transaction upserts every entry by normalised key, merges rows that only differ in
/// notation, deletes unreferenced rows the server doesn't know and marks referenced ones Missing (D7).
/// </summary>
public class MinecraftCatalogScanService : IMinecraftCatalogScanService
{
    public const int SchemaVersion = 1;
    public const int MaxEntries = 10_000;

    /// <summary>A scan with no snapshot after this long no longer blocks a new one.</summary>
    public static readonly TimeSpan ScanTimeout = TimeSpan.FromMinutes(15);

    private static readonly MinecraftCatalogScanStatus[] ActiveStatuses =
    {
        MinecraftCatalogScanStatus.Requested, MinecraftCatalogScanStatus.Running, MinecraftCatalogScanStatus.Ingesting,
    };

    private readonly KnKDbContext _db;
    private readonly IMinecraftIconResolver _icons;
    private readonly ILogger<MinecraftCatalogScanService> _logger;
    private readonly Func<DateTime> _utcNow;

    public MinecraftCatalogScanService(KnKDbContext db, IMinecraftIconResolver icons, ILogger<MinecraftCatalogScanService> logger)
        : this(db, icons, logger, () => DateTime.UtcNow)
    {
    }

    /// <param name="utcNow">The clock (tests).</param>
    public MinecraftCatalogScanService(KnKDbContext db, IMinecraftIconResolver icons, ILogger<MinecraftCatalogScanService> logger, Func<DateTime> utcNow)
    {
        _db = db;
        _icons = icons;
        _logger = logger;
        _utcNow = utcNow;
    }

    public async Task<MinecraftCatalogScanDto> RequestScanAsync(int requestedByUserId, CancellationToken ct = default)
    {
        await using var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;
        var now = _utcNow();

        var active = await _db.MinecraftCatalogScans.Where(s => ActiveStatuses.Contains(s.Status)).ToListAsync(ct);
        foreach (var stale in active.Where(s => now - s.CreatedAt > ScanTimeout))
        {
            await FailAsync(stale, $"Timed out: no snapshot from the Minecraft server within {ScanTimeout.TotalMinutes:0} minutes.", ct);
        }
        var stillActive = active.FirstOrDefault(s => ActiveStatuses.Contains(s.Status));
        if (stillActive != null)
        {
            await _db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            throw new MinecraftCatalogConflictException($"Scan {stillActive.Id} is still {stillActive.Status}; wait for it or cancel it.");
        }

        var scan = new MinecraftCatalogScan { RequestedByUserId = requestedByUserId, CreatedAt = now };
        var session = new WorkflowSession
        {
            UserId = requestedByUserId,
            EntityTypeName = "MinecraftCatalogScan",
            CreatedAt = now,
        };
        var task = new WorldTask
        {
            WorkflowSession = session,
            TaskType = WorldTaskTypes.MinecraftCatalogScan,
            Status = "Pending",
            CreatedAt = now,
        };
        _db.MinecraftCatalogScans.Add(scan);
        _db.WorldTasks.Add(task);
        await _db.SaveChangesAsync(ct);

        scan.WorldTaskId = task.Id;
        session.EntityId = scan.Id;
        task.InputJson = JsonSerializer.Serialize(new { scanId = scan.Id });
        await _db.SaveChangesAsync(ct);
        if (transaction != null) await transaction.CommitAsync(ct);

        _logger.LogInformation("Minecraft catalogue scan {ScanId} requested by user {UserId} (WorldTask {TaskId})", scan.Id, requestedByUserId, task.Id);
        return ToDto(scan);
    }

    public async Task<IReadOnlyList<MinecraftCatalogScanDto>> ListAsync(int take, CancellationToken ct = default)
    {
        var scans = await _db.MinecraftCatalogScans.AsNoTracking()
            .OrderByDescending(s => s.Id)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(ct);
        return scans.Select(ToDto).ToList();
    }

    public async Task<MinecraftCatalogScanDto?> GetAsync(int scanId, CancellationToken ct = default)
    {
        var scan = await _db.MinecraftCatalogScans.FirstOrDefaultAsync(s => s.Id == scanId, ct);
        if (scan == null) return null;

        if (ActiveStatuses.Contains(scan.Status) && scan.WorldTaskId is { } taskId)
        {
            var task = await _db.WorldTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
            if (task?.Status == "Failed")
            {
                scan.Status = MinecraftCatalogScanStatus.Failed;
                scan.Error = $"The Minecraft server reported: {task.ErrorMessage ?? "unknown error"}";
                scan.CompletedAt = _utcNow();
                await SaveUnlessChangedMeanwhileAsync(scan, ct);
            }
            else if (task?.Status == "InProgress" && scan.Status == MinecraftCatalogScanStatus.Requested)
            {
                scan.Status = MinecraftCatalogScanStatus.Running;
                await SaveUnlessChangedMeanwhileAsync(scan, ct);
            }
        }
        return ToDto(scan);
    }

    public async Task<MinecraftCatalogScanDto> CancelAsync(int scanId, CancellationToken ct = default)
    {
        var scan = await _db.MinecraftCatalogScans.FirstOrDefaultAsync(s => s.Id == scanId, ct)
            ?? throw new KeyNotFoundException($"Minecraft catalogue scan {scanId} not found.");
        if (scan.Status == MinecraftCatalogScanStatus.Ingesting)
        {
            throw new MinecraftCatalogConflictException($"Scan {scanId} is being applied and can't be cancelled.");
        }
        if (!ActiveStatuses.Contains(scan.Status))
        {
            throw new MinecraftCatalogConflictException($"Scan {scanId} is already {scan.Status}.");
        }
        await FailAsync(scan, "Cancelled by staff.", ct);
        if (!await SaveUnlessChangedMeanwhileAsync(scan, ct))
        {
            throw new MinecraftCatalogConflictException($"Scan {scanId} changed meanwhile and is now {scan.Status}.");
        }
        return ToDto(scan);
    }

    public async Task<MinecraftCatalogScanDto> IngestAsync(int scanId, MinecraftCatalogSnapshotDto snapshot, CancellationToken ct = default)
    {
        // 1. Validation, before any write.
        var entries = Validate(snapshot);
        var hash = MinecraftCatalogSnapshotHash.Compute(snapshot.Entries!);
        if (!string.Equals(hash, snapshot.ContentHash?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"contentHash does not match the entries (expected {hash}).");
        }

        var scan = await _db.MinecraftCatalogScans.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scanId, ct)
            ?? throw new KeyNotFoundException($"Minecraft catalogue scan {scanId} not found.");
        // 2. Idempotency: a retry of the same snapshot gets the stored result.
        if (scan.Status == MinecraftCatalogScanStatus.Completed)
        {
            if (string.Equals(scan.ContentHash, hash, StringComparison.Ordinal)) return ToDto(scan);
            throw new MinecraftCatalogConflictException($"Scan {scanId} was already completed with a different snapshot.");
        }
        EnsureCanIngest(scan);

        // 3. Icons, outside the transaction.
        var icons = await _icons.ResolveAsync(snapshot.MinecraftVersion,
            entries.Select(e => new MinecraftIconRequest(e.Key, e.Entry.IsItem, e.Entry.IsBlock)).ToList(), ct);

        // 4. One transaction.
        try
        {
            await using var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;
            var tracked = _db.Database.IsRelational()
                ? await _db.MinecraftCatalogScans
                    .FromSqlRaw("SELECT * FROM minecraft_catalog_scans WHERE Id = {0} FOR UPDATE", scanId)
                    .SingleAsync(ct)
                : await _db.MinecraftCatalogScans.SingleAsync(s => s.Id == scanId, ct);
            EnsureCanIngest(tracked);
            tracked.Status = MinecraftCatalogScanStatus.Ingesting;

            await ApplyAsync(tracked, snapshot, entries, icons, ct);

            tracked.MinecraftVersion = Truncate(snapshot.MinecraftVersion, 32);
            tracked.ServerVersion = Truncate(snapshot.ServerVersion, 191);
            tracked.SchemaVersion = snapshot.SchemaVersion;
            tracked.ContentHash = hash;
            tracked.EntryCount = entries.Count;
            tracked.Status = MinecraftCatalogScanStatus.Completed;
            tracked.CompletedAt = _utcNow();
            tracked.Error = null;
            await _db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);

            _logger.LogInformation(
                "Minecraft catalogue scan {ScanId} ({Version}) applied: {Inserted} inserted, {Updated} updated, {Unchanged} unchanged, {Merged} merged, {Deleted} deleted, {Missing} marked missing, {IconsMissing} without icon",
                scanId, tracked.MinecraftVersion, tracked.Inserted, tracked.Updated, tracked.Unchanged, tracked.Merged, tracked.Deleted, tracked.MarkedMissing, tracked.IconsMissing);
            return ToDto(tracked);
        }
        catch (Exception ex) when (ex is not MinecraftCatalogConflictException and not OperationCanceledException)
        {
            // Rolled back: record the failure on a clean context so the admin page shows it.
            _db.ChangeTracker.Clear();
            var failed = await _db.MinecraftCatalogScans.FirstOrDefaultAsync(s => s.Id == scanId, CancellationToken.None);
            if (failed != null && failed.Status != MinecraftCatalogScanStatus.Completed)
            {
                failed.Status = MinecraftCatalogScanStatus.Failed;
                failed.Error = $"Applying the snapshot failed: {ex.Message}";
                failed.CompletedAt = _utcNow();
                await _db.SaveChangesAsync(CancellationToken.None);
            }
            _logger.LogError(ex, "Minecraft catalogue scan {ScanId} could not be applied", scanId);
            throw;
        }
    }

    public async Task SetIconOverrideAsync(int materialId, string? iconUrlOverride, CancellationToken ct = default)
    {
        var material = await _db.MinecraftMaterialRefs.FirstOrDefaultAsync(m => m.Id == materialId, ct)
            ?? throw new KeyNotFoundException($"MinecraftMaterialRef with id {materialId} not found.");
        var url = string.IsNullOrWhiteSpace(iconUrlOverride) ? null : iconUrlOverride.Trim();
        if (url != null && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
        {
            throw new ArgumentException("iconUrlOverride must be an http(s) URL.");
        }
        material.IconUrlOverride = url;
        await _db.SaveChangesAsync(ct);
    }

    private sealed record ValidEntry(string Key, MinecraftCatalogEntryDto Entry);

    private static List<ValidEntry> Validate(MinecraftCatalogSnapshotDto? snapshot)
    {
        if (snapshot == null) throw new ArgumentException("A snapshot is required.");
        if (snapshot.SchemaVersion != SchemaVersion)
        {
            throw new ArgumentException($"schemaVersion {snapshot.SchemaVersion} is not supported (expected {SchemaVersion}).");
        }
        if (snapshot.Entries == null || snapshot.Entries.Count == 0) throw new ArgumentException("The snapshot has no entries.");
        if (snapshot.Entries.Count > MaxEntries) throw new ArgumentException($"The snapshot has more than {MaxEntries} entries.");

        var valid = new List<ValidEntry>(snapshot.Entries.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in snapshot.Entries)
        {
            var key = MinecraftNamespaceKey.TryNormalize(entry?.Key)
                ?? throw new ArgumentException($"\"{entry?.Key}\" is not a valid namespaced key.");
            if (!seen.Add(key)) throw new ArgumentException($"The key {key} occurs more than once.");
            valid.Add(new ValidEntry(key, entry!));
        }
        return valid;
    }

    private static void EnsureCanIngest(MinecraftCatalogScan scan)
    {
        if (scan.Status is not (MinecraftCatalogScanStatus.Requested or MinecraftCatalogScanStatus.Running))
        {
            throw new MinecraftCatalogConflictException($"Scan {scan.Id} is {scan.Status} and takes no snapshot.");
        }
    }

    private async Task ApplyAsync(
        MinecraftCatalogScan scan,
        MinecraftCatalogSnapshotDto snapshot,
        List<ValidEntry> entries,
        IReadOnlyDictionary<string, MinecraftIconResult> icons,
        CancellationToken ct)
    {
        var version = Truncate(snapshot.MinecraftVersion, 32);
        var materials = await _db.MinecraftMaterialRefs.ToListAsync(ct);
        var byNormalizedKey = materials
            .Where(m => MinecraftNamespaceKey.TryNormalize(m.NamespaceKey) != null)
            .GroupBy(m => MinecraftNamespaceKey.TryNormalize(m.NamespaceKey)!, StringComparer.Ordinal)
            // The row whose key already matches (case aside, as the unique index compares) is kept; the others merge into it.
            .ToDictionary(g => g.Key, g => g.OrderBy(m => string.Equals(m.NamespaceKey, g.Key, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(m => m.Id).ToList(), StringComparer.Ordinal);

        var merged = new List<MinecraftMaterialRef>();
        foreach (var (key, entry) in entries)
        {
            byNormalizedKey.TryGetValue(key, out var rows);
            var material = rows?.FirstOrDefault();
            var isNew = material == null;
            if (material == null)
            {
                material = new MinecraftMaterialRef
                {
                    NamespaceKey = key,
                    Category = entry.IsBlock ? "BLOCK" : "ITEM",
                    FirstSeenVersion = version,
                };
                _db.MinecraftMaterialRefs.Add(material);
            }
            else
            {
                // Rows that only differ in notation are merged into the first one (D7).
                foreach (var duplicate in rows!.Skip(1))
                {
                    await MinecraftCatalogMaintenanceService.RepointReferencesAsync(_db, duplicate.Id, material, ct);
                    _db.MinecraftMaterialRefs.Remove(duplicate);
                    merged.Add(duplicate);
                }
                if (rows.Count > 1)
                {
                    // Delete the duplicates before the kept row takes the normal key, so the unique index never sees both.
                    await _db.SaveChangesAsync(ct);
                }
            }

            var before = Facts(material);
            material.NamespaceKey = key;
            material.DisplayName = Truncate(entry.DisplayName, 191) ?? MinecraftNamespaceKey.DisplayNameFromKey(key);
            material.IsItem = entry.IsItem;
            material.IsBlock = entry.IsBlock;
            material.MaxStackSize = entry.MaxStackSize;
            material.DefaultBlockState = entry.IsBlock ? entry.DefaultBlockState : null;
            material.CatalogStatus = MinecraftCatalogStatus.Active;
            material.FirstSeenVersion ??= version;
            material.LastSeenVersion = version;
            material.LastScanId = scan.Id;
            if (icons.TryGetValue(key, out var icon))
            {
                switch (icon.Outcome)
                {
                    case MinecraftIconOutcome.Resolved:
                        material.IconUrl = icon.Url;
                        material.IconSource = Truncate(icon.Source, 64);
                        material.IconStatus = MinecraftIconStatus.Resolved;
                        break;
                    case MinecraftIconOutcome.Missing:
                        material.IconUrl = null;
                        material.IconSource = Truncate(icon.Source, 64);
                        material.IconStatus = MinecraftIconStatus.Missing;
                        break;
                    // Unavailable: a source couldn't be reached; keep what the row had.
                }
            }

            if (isNew) scan.Inserted++;
            else if (before == Facts(material)) scan.Unchanged++;
            else scan.Updated++;
            if (material.IconStatus == MinecraftIconStatus.Missing && string.IsNullOrWhiteSpace(material.IconUrlOverride)) scan.IconsMissing++;
        }
        scan.Merged = merged.Count;
        await _db.SaveChangesAsync(ct);

        // Rows the server doesn't know: delete when nothing references them, else keep as Missing (D3, D7).
        var scannedKeys = entries.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var unseen = materials
            .Where(m => !merged.Contains(m))
            .Where(m => MinecraftNamespaceKey.TryNormalize(m.NamespaceKey) is not { } key || !scannedKeys.Contains(key))
            .ToList();
        if (unseen.Count == 0) return;

        var references = await MinecraftMaterialReferences.CountAsync(_db, unseen.Select(m => m.Id).ToList(), ct);
        foreach (var material in unseen)
        {
            if (references.ContainsKey(material.Id))
            {
                if (material.CatalogStatus != MinecraftCatalogStatus.Missing)
                {
                    material.CatalogStatus = MinecraftCatalogStatus.Missing;
                }
                scan.MarkedMissing++;
            }
            else
            {
                _db.MinecraftMaterialRefs.Remove(material);
                scan.Deleted++;
                _logger.LogInformation("Minecraft catalogue scan {ScanId} deleted unreferenced material {Id} ({Key})", scan.Id, material.Id, material.NamespaceKey);
            }
        }
    }

    /// <summary>
    /// Saves a status change unless the scan's status changed in the database meanwhile (an ingest that
    /// completed it); then the scan is reloaded and false returned.
    /// </summary>
    private async Task<bool> SaveUnlessChangedMeanwhileAsync(MinecraftCatalogScan scan, CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            var current = await _db.MinecraftCatalogScans.AsNoTracking().FirstAsync(s => s.Id == scan.Id, ct);
            _db.Entry(scan).CurrentValues.SetValues(current);
            return false;
        }
    }

    /// <summary>The scan-owned facts of a row, to tell an update from an unchanged row (provenance excluded).</summary>
    private static string Facts(MinecraftMaterialRef m) => string.Join('|',
        m.NamespaceKey, m.DisplayName, m.IsItem, m.IsBlock, m.MaxStackSize, m.DefaultBlockState, m.CatalogStatus, m.IconUrl, m.IconStatus);

    private async Task FailAsync(MinecraftCatalogScan scan, string error, CancellationToken ct)
    {
        scan.Status = MinecraftCatalogScanStatus.Failed;
        scan.Error = error;
        scan.CompletedAt = _utcNow();
        if (scan.WorldTaskId is { } taskId)
        {
            var task = await _db.WorldTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
            if (task != null && task.Status is "Pending" or "InProgress")
            {
                task.Status = "Cancelled";
                task.ErrorMessage = error;
                task.UpdatedAt = _utcNow();
            }
        }
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    internal static MinecraftCatalogScanDto ToDto(MinecraftCatalogScan scan) => new()
    {
        Id = scan.Id,
        Status = scan.Status.ToString(),
        RequestedByUserId = scan.RequestedByUserId,
        WorldTaskId = scan.WorldTaskId,
        MinecraftVersion = scan.MinecraftVersion,
        ServerVersion = scan.ServerVersion,
        EntryCount = scan.EntryCount,
        Inserted = scan.Inserted,
        Updated = scan.Updated,
        Unchanged = scan.Unchanged,
        Merged = scan.Merged,
        Deleted = scan.Deleted,
        MarkedMissing = scan.MarkedMissing,
        IconsMissing = scan.IconsMissing,
        Error = scan.Error,
        CreatedAt = scan.CreatedAt,
        CompletedAt = scan.CompletedAt,
    };
}
