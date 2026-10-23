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
    /// <summary>The newest snapshot schema; schema 1 (materials only) is still accepted (KNG-250 E6).</summary>
    public const int SchemaVersion = 2;
    public const int MaxEntries = 10_000;
    public const int MaxEnchantments = 1_000;
    public const string EnchantedBookKey = "minecraft:enchanted_book";

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
        var enchantments = ValidateEnchantments(snapshot);
        var hash = MinecraftCatalogSnapshotHash.Compute(snapshot.Entries!, enchantments == null ? null : snapshot.Enchantments);
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
            if (enchantments != null)
            {
                await ApplyEnchantmentsAsync(tracked, snapshot, enchantments, icons, ct);
                tracked.EnchantmentCount = enchantments.Count;
            }

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
            if (enchantments != null)
            {
                _logger.LogInformation(
                    "Minecraft catalogue scan {ScanId} enchantments: {Count} scanned, {Inserted} inserted, {Updated} updated, {Unchanged} unchanged, {Merged} merged, {Deleted} deleted, {Missing} marked missing; definitions {Created} created, {Linked} linked",
                    scanId, tracked.EnchantmentCount, tracked.EnchantmentsInserted, tracked.EnchantmentsUpdated, tracked.EnchantmentsUnchanged, tracked.EnchantmentsMerged,
                    tracked.EnchantmentsDeleted, tracked.EnchantmentsMarkedMissing, tracked.EnchantmentDefinitionsCreated, tracked.EnchantmentDefinitionsLinked);
            }
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
        if (snapshot.SchemaVersion is < 1 or > SchemaVersion)
        {
            throw new ArgumentException($"schemaVersion {snapshot.SchemaVersion} is not supported (expected 1 to {SchemaVersion}).");
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

    private sealed record ValidEnchantment(string Key, MinecraftCatalogEnchantmentDto Entry);

    /// <summary>The schema 2 enchantments (§12.2), or null for a schema 1 snapshot.</summary>
    private static List<ValidEnchantment>? ValidateEnchantments(MinecraftCatalogSnapshotDto snapshot)
    {
        if (snapshot.SchemaVersion < 2) return null;
        if (snapshot.Enchantments == null) throw new ArgumentException("A schema 2 snapshot needs an enchantments list (it may be empty).");
        if (snapshot.Enchantments.Count > MaxEnchantments) throw new ArgumentException($"The snapshot has more than {MaxEnchantments} enchantments.");

        var valid = new List<ValidEnchantment>(snapshot.Enchantments.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in snapshot.Enchantments)
        {
            if (entry == null) throw new ArgumentException("The snapshot has an empty enchantment.");
            var key = MinecraftEnchantmentKey.TryNormalize(entry.Key, entry.IsCustom)
                ?? throw new ArgumentException($"\"{entry.Key}\" is not a valid enchantment key.");
            if (entry.IsCustom && !MinecraftEnchantmentKey.IsCustomKey(key))
            {
                throw new ArgumentException($"The custom enchantment {key} must not use the minecraft namespace.");
            }
            if (!seen.Add(key)) throw new ArgumentException($"The enchantment {key} occurs more than once.");
            valid.Add(new ValidEnchantment(key, entry));
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
    /// KNG-250 §12.4: upserts the scanned enchantments by key (§12.3), merges notation duplicates, deletes
    /// unreferenced unseen refs and marks referenced ones Missing, then gives every scanned enchantment an
    /// EnchantmentDefinition (an unlinked matching one, else a new one). Definitions are never deleted.
    /// </summary>
    private async Task ApplyEnchantmentsAsync(
        MinecraftCatalogScan scan,
        MinecraftCatalogSnapshotDto snapshot,
        List<ValidEnchantment> enchantments,
        IReadOnlyDictionary<string, MinecraftIconResult> icons,
        CancellationToken ct)
    {
        var version = Truncate(snapshot.MinecraftVersion, 32);
        var bookIcon = icons.TryGetValue(EnchantedBookKey, out var book) && book.Outcome == MinecraftIconOutcome.Resolved ? book.Url : null;
        var scanned = new ScannedEnchantmentKeys(enchantments.Select(e => (e.Key, e.Entry.IsCustom)));
        var refs = await _db.MinecraftEnchantmentRefs.ToListAsync(ct);
        var definitions = await _db.EnchantmentDefinitions.ToListAsync(ct);

        var byKey = refs
            .Select(r => (Row: r, Key: MinecraftEnchantmentKey.Resolve(r.NamespaceKey, r.IsCustom, scanned)))
            .Where(x => x.Key != null && scanned.Contains(x.Key))
            .GroupBy(x => x.Key!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Row)
                .OrderBy(r => string.Equals(r.NamespaceKey, g.Key, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(r => r.Id).ToList(), StringComparer.Ordinal);

        var merged = new List<MinecraftEnchantmentRef>();
        var rowsByKey = new Dictionary<string, MinecraftEnchantmentRef>(StringComparer.Ordinal);
        var previousMaxLevel = refs.ToDictionary(r => r.Id, r => r.MaxLevel);
        foreach (var (key, entry) in enchantments)
        {
            byKey.TryGetValue(key, out var rows);
            var row = rows?.FirstOrDefault();
            var isNew = row == null;
            if (row == null)
            {
                row = new MinecraftEnchantmentRef { NamespaceKey = key, FirstSeenVersion = version };
                _db.MinecraftEnchantmentRefs.Add(row);
            }
            else if (rows!.Count > 1)
            {
                foreach (var duplicate in rows.Skip(1))
                {
                    foreach (var definition in definitions.Where(d => d.MinecraftEnchantmentRefId == duplicate.Id))
                    {
                        definition.MinecraftEnchantmentRefId = row.Id;
                    }
                    _db.MinecraftEnchantmentRefs.Remove(duplicate);
                    merged.Add(duplicate);
                }
                // Delete the duplicates before the kept row takes the normal key, so the unique index never sees both.
                await _db.SaveChangesAsync(ct);
            }

            var before = Facts(row);
            row.NamespaceKey = key;
            row.DisplayName = Truncate(entry.DisplayName, 191) ?? MinecraftNamespaceKey.DisplayNameFromKey(key);
            row.MaxLevel = entry.MaxLevel;
            row.IsCustom = entry.IsCustom;
            row.Category = Truncate(entry.Category, 191) ?? row.Category;
            row.CatalogStatus = MinecraftCatalogStatus.Active;
            row.FirstSeenVersion ??= version;
            row.LastSeenVersion = version;
            row.LastScanId = scan.Id;
            // E5: the enchanted book, unless someone set another picture by hand.
            if (bookIcon != null && (string.IsNullOrWhiteSpace(row.IconUrl) || row.IconUrl.EndsWith("/enchanted_book.png", StringComparison.OrdinalIgnoreCase)))
            {
                row.IconUrl = bookIcon;
            }
            rowsByKey[key] = row;

            if (isNew) scan.EnchantmentsInserted++;
            else if (before == Facts(row)) scan.EnchantmentsUnchanged++;
            else scan.EnchantmentsUpdated++;
        }
        scan.EnchantmentsMerged = merged.Count;
        await _db.SaveChangesAsync(ct);

        // Refs the server doesn't know: delete when no definition uses them, else keep as Missing.
        var referenced = definitions.Where(d => d.MinecraftEnchantmentRefId != null).Select(d => d.MinecraftEnchantmentRefId!.Value).ToHashSet();
        foreach (var row in refs.Where(r => !merged.Contains(r) && !rowsByKey.Values.Contains(r)))
        {
            if (referenced.Contains(row.Id))
            {
                row.CatalogStatus = MinecraftCatalogStatus.Missing;
                scan.EnchantmentsMarkedMissing++;
            }
            else
            {
                _db.MinecraftEnchantmentRefs.Remove(row);
                scan.EnchantmentsDeleted++;
                _logger.LogInformation("Minecraft catalogue scan {ScanId} deleted unreferenced enchantment {Id} ({Key})", scan.Id, row.Id, row.NamespaceKey);
            }
        }

        // E3: unlinked definitions of a scanned enchantment are linked to it, and one without any gets a new one.
        foreach (var (key, entry) in enchantments)
        {
            var row = rowsByKey[key];
            var linked = definitions.Where(d => d.MinecraftEnchantmentRefId == row.Id).ToList();

            // A vanilla definition still at its ref's previous level (as seeded) follows the server's maximum; one
            // set to another level by staff keeps it.
            if (entry.MaxLevel is { } serverMax && previousMaxLevel.GetValueOrDefault(row.Id) is { } oldMax)
            {
                foreach (var definition in linked.Where(d => !d.IsCustom && d.MaxLevel == oldMax && d.MaxLevel != serverMax))
                {
                    definition.MaxLevel = Math.Max(1, serverMax);
                }
            }

            var matches = definitions
                .Where(d => d.MinecraftEnchantmentRefId == null && d.IsCustom == entry.IsCustom)
                .Where(d => MinecraftEnchantmentKey.Resolve(d.Key, d.IsCustom, scanned) == key)
                .ToList();
            foreach (var match in matches)
            {
                match.MinecraftEnchantmentRefId = row.Id;
                scan.EnchantmentDefinitionsLinked++;
            }
            if (linked.Count > 0 || matches.Count > 0) continue;

            var created = new EnchantmentDefinition
            {
                // Custom definitions are keyed by the ability key (AbilityDefinition seed), vanilla ones by the namespaced key.
                Key = entry.IsCustom ? key[(key.IndexOf(':') + 1)..] : key,
                DisplayName = row.DisplayName ?? MinecraftNamespaceKey.DisplayNameFromKey(key),
                Description = string.Empty,
                IsCustom = entry.IsCustom,
                MaxLevel = Math.Max(1, entry.MaxLevel ?? 1),
                MinecraftEnchantmentRefId = row.Id,
            };
            _db.EnchantmentDefinitions.Add(created);
            definitions.Add(created);
            scan.EnchantmentDefinitionsCreated++;
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

    private static string Facts(MinecraftEnchantmentRef e) => string.Join('|',
        e.NamespaceKey, e.DisplayName, e.MaxLevel, e.IsCustom, e.Category, e.CatalogStatus, e.IconUrl);

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
        EnchantmentCount = scan.EnchantmentCount,
        EnchantmentsInserted = scan.EnchantmentsInserted,
        EnchantmentsUpdated = scan.EnchantmentsUpdated,
        EnchantmentsUnchanged = scan.EnchantmentsUnchanged,
        EnchantmentsMerged = scan.EnchantmentsMerged,
        EnchantmentsDeleted = scan.EnchantmentsDeleted,
        EnchantmentsMarkedMissing = scan.EnchantmentsMarkedMissing,
        EnchantmentDefinitionsCreated = scan.EnchantmentDefinitionsCreated,
        EnchantmentDefinitionsLinked = scan.EnchantmentDefinitionsLinked,
        Error = scan.Error,
        CreatedAt = scan.CreatedAt,
        CompletedAt = scan.CompletedAt,
    };
}
