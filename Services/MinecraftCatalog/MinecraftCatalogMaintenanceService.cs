using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.MinecraftCatalog;

/// <summary>
/// KNG-200 phase 2 (docs/specs/minecraft-catalog/IMPLEMENTATION_PLAN.md §8): the backfill that brings the
/// existing rows into the catalogue shape, the read-only audit, and the merge action. Nothing here
/// deletes a row except <see cref="MergeAsync"/>, and only after every reference has moved.
/// </summary>
public class MinecraftCatalogMaintenanceService : IMinecraftCatalogMaintenanceService
{
    // Category for a material the backfill creates for an unlinked block ref; the scan sets the real flags.
    private const string CreatedForBlockCategory = "BLOCK";

    private readonly KnKDbContext _db;
    private readonly IMinecraftMaterialCatalogService? _staticCatalog;
    private readonly ILogger<MinecraftCatalogMaintenanceService> _logger;

    /// <param name="staticCatalog">Only used for display names until phase 5 removes it.</param>
    public MinecraftCatalogMaintenanceService(
        KnKDbContext db,
        IMinecraftMaterialCatalogService? staticCatalog,
        ILogger<MinecraftCatalogMaintenanceService> logger)
    {
        _db = db;
        _staticCatalog = staticCatalog;
        _logger = logger;
    }

    public async Task<MinecraftCatalogBackfillResultDto> BackfillAsync(CancellationToken ct = default)
    {
        var result = new MinecraftCatalogBackfillResultDto();
        var materials = await _db.MinecraftMaterialRefs.ToListAsync(ct);
        var staticNames = StaticDisplayNames();

        foreach (var material in materials.OrderBy(m => m.Id))
        {
            var normalized = MinecraftNamespaceKey.TryNormalize(material.NamespaceKey);
            if (normalized == null)
            {
                result.Skipped.Add($"MinecraftMaterialRef {material.Id} \"{material.NamespaceKey}\": not a valid key");
                continue;
            }
            if (normalized == material.NamespaceKey) continue;

            // Compared case-insensitively, like the MySQL collation of the unique index.
            var otherHasKey = materials.Any(m => m.Id != material.Id && string.Equals(m.NamespaceKey, normalized, StringComparison.OrdinalIgnoreCase));
            if (otherHasKey)
            {
                result.Skipped.Add($"MinecraftMaterialRef {material.Id} \"{material.NamespaceKey}\": {normalized} is taken, merge needed");
                continue;
            }
            material.NamespaceKey = normalized;
            result.MaterialKeysNormalized++;
        }

        foreach (var material in materials.Where(m => string.IsNullOrWhiteSpace(m.DisplayName)))
        {
            var key = MinecraftNamespaceKey.TryNormalize(material.NamespaceKey);
            if (key == null) continue;
            material.DisplayName = staticNames.GetValueOrDefault(key) ?? MinecraftNamespaceKey.DisplayNameFromKey(key);
            result.MaterialDisplayNamesFilled++;
        }

        var materialsByKey = materials
            .Where(m => MinecraftNamespaceKey.TryNormalize(m.NamespaceKey) != null)
            .GroupBy(m => MinecraftNamespaceKey.TryNormalize(m.NamespaceKey)!)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Id).First());
        var materialsById = materials.ToDictionary(m => m.Id);

        var blocks = await _db.MinecraftBlockRefs.ToListAsync(ct);
        foreach (var block in blocks)
        {
            var canonical = MinecraftBlockState.TryCanonicalize(block.NamespaceKey, block.BlockStateString);
            if (canonical == null)
            {
                result.Skipped.Add($"MinecraftBlockRef {block.Id} \"{block.NamespaceKey}\" \"{block.BlockStateString}\": not a valid key or state");
                continue;
            }

            if (block.MaterialRefId == null)
            {
                if (!materialsByKey.TryGetValue(canonical.NamespaceKey, out var material))
                {
                    material = new MinecraftMaterialRef
                    {
                        NamespaceKey = canonical.NamespaceKey,
                        Category = CreatedForBlockCategory,
                        DisplayName = staticNames.GetValueOrDefault(canonical.NamespaceKey)
                            ?? MinecraftNamespaceKey.DisplayNameFromKey(canonical.NamespaceKey),
                        CatalogStatus = MinecraftCatalogStatus.Unverified,
                    };
                    _db.MinecraftMaterialRefs.Add(material);
                    materialsByKey[canonical.NamespaceKey] = material;
                    result.MaterialsCreated++;
                }
                block.MaterialRef = material;
                result.BlockRefsLinked++;
            }
            else if (materialsById.TryGetValue(block.MaterialRefId.Value, out var linked)
                     && MinecraftNamespaceKey.TryNormalize(linked.NamespaceKey) is { } linkedKey
                     && linkedKey != canonical.NamespaceKey)
            {
                result.Skipped.Add($"MinecraftBlockRef {block.Id}: key {canonical.NamespaceKey} differs from its material {linkedKey}");
                continue;
            }

            var stateKey = MinecraftBlockState.StateKey(canonical);
            if (block.NamespaceKey != canonical.NamespaceKey || block.BlockStateString != canonical.State || block.StateKey != stateKey)
            {
                block.NamespaceKey = canonical.NamespaceKey;
                block.BlockStateString = canonical.State;
                block.StateKey = stateKey;
                result.BlockRefsCanonicalized++;
            }
        }

        await _db.SaveChangesAsync(ct);
        foreach (var skipped in result.Skipped)
        {
            _logger.LogWarning("Minecraft catalogue backfill skipped {Row}", skipped);
        }
        if (result.MaterialKeysNormalized + result.MaterialDisplayNamesFilled + result.MaterialsCreated + result.BlockRefsLinked + result.BlockRefsCanonicalized > 0)
        {
            _logger.LogInformation(
                "Minecraft catalogue backfill: {Normalized} keys normalised, {Names} display names, {Created} materials created, {Linked} block refs linked, {Canonical} block refs canonicalised",
                result.MaterialKeysNormalized, result.MaterialDisplayNamesFilled, result.MaterialsCreated, result.BlockRefsLinked, result.BlockRefsCanonicalized);
        }
        return result;
    }

    public async Task<MinecraftCatalogAuditDto> AuditAsync(CancellationToken ct = default)
    {
        var materials = await _db.MinecraftMaterialRefs.AsNoTracking().OrderBy(m => m.Id).ToListAsync(ct);
        var blocks = await _db.MinecraftBlockRefs.AsNoTracking().OrderBy(b => b.Id).ToListAsync(ct);
        var references = await MinecraftMaterialReferences.CountAsync(_db, null, ct);
        var materialsById = materials.ToDictionary(m => m.Id);

        MinecraftCatalogAuditMaterialDto Row(MinecraftMaterialRef m)
        {
            var refs = references.GetValueOrDefault(m.Id) ?? new Dictionary<string, int>();
            return new MinecraftCatalogAuditMaterialDto
            {
                Id = m.Id,
                NamespaceKey = m.NamespaceKey,
                NormalizedKey = MinecraftNamespaceKey.TryNormalize(m.NamespaceKey),
                DisplayName = m.DisplayName,
                CatalogStatus = m.CatalogStatus.ToString(),
                References = refs,
                TotalReferences = refs.Values.Sum(),
                IconUrl = m.IconUrlOverride ?? m.IconUrl,
            };
        }

        static MinecraftCatalogAuditBlockDto BlockRow(MinecraftBlockRef b) => new()
        {
            Id = b.Id,
            NamespaceKey = b.NamespaceKey,
            BlockStateString = b.BlockStateString,
            MaterialRefId = b.MaterialRefId,
            StateKey = b.StateKey,
        };

        var report = new MinecraftCatalogAuditDto
        {
            MaterialCount = materials.Count,
            UnverifiedCount = materials.Count(m => m.CatalogStatus == MinecraftCatalogStatus.Unverified),
            BlockRefCount = blocks.Count,
        };

        var materialRows = materials.Select(Row).ToList();
        report.InvalidMaterialKeys = materialRows.Where(r => r.NormalizedKey == null).ToList();
        report.MaterialKeyCollisions = materialRows
            .Where(r => r.NormalizedKey != null)
            .GroupBy(r => r.NormalizedKey!)
            .Where(g => g.Count() > 1)
            .Select(g => g.ToList())
            .ToList();
        var colliding = report.MaterialKeyCollisions.SelectMany(g => g).Select(r => r.Id).ToHashSet();
        report.NotNormalizedMaterialKeys = materialRows
            .Where(r => r.NormalizedKey != null && r.NormalizedKey != r.NamespaceKey && !colliding.Contains(r.Id))
            .ToList();
        report.MissingIcons = materials
            .Where(m => m.CatalogStatus == MinecraftCatalogStatus.Active && m.IconStatus == MinecraftIconStatus.Missing
                && string.IsNullOrWhiteSpace(m.IconUrlOverride))
            .Select(Row)
            .ToList();
        report.IconOverrides = materials
            .Where(m => !string.IsNullOrWhiteSpace(m.IconUrlOverride))
            .Select(Row)
            .ToList();
        report.MissingButReferenced = materialRows
            .Where(r => r.CatalogStatus == nameof(MinecraftCatalogStatus.Missing) && r.TotalReferences > 0)
            .ToList();

        var stateKeys = new Dictionary<int, string>();
        foreach (var block in blocks)
        {
            var canonical = MinecraftBlockState.TryCanonicalize(block.NamespaceKey, block.BlockStateString);
            if (canonical == null)
            {
                report.InvalidBlockStates.Add(BlockRow(block));
                continue;
            }
            var stateKey = MinecraftBlockState.StateKey(canonical);
            stateKeys[block.Id] = stateKey;

            if (block.MaterialRefId == null)
            {
                report.UnlinkedBlockRefs.Add(BlockRow(block));
            }
            var materialKey = block.MaterialRefId is { } materialId && materialsById.TryGetValue(materialId, out var material)
                ? MinecraftNamespaceKey.TryNormalize(material.NamespaceKey)
                : null;
            if (block.NamespaceKey != canonical.NamespaceKey || block.BlockStateString != canonical.State || block.StateKey != stateKey
                || (block.MaterialRefId != null && materialKey != canonical.NamespaceKey))
            {
                report.NotCanonicalBlockRefs.Add(BlockRow(block));
            }
        }
        report.DuplicateBlockStates = blocks
            .Where(b => stateKeys.ContainsKey(b.Id))
            .GroupBy(b => stateKeys[b.Id])
            .Where(g => g.Count() > 1)
            .Select(g => g.Select(BlockRow).ToList())
            .ToList();

        report.ReadyForConstraints = report.MaterialKeyCollisions.Count == 0
            && report.InvalidMaterialKeys.Count == 0
            && report.DuplicateBlockStates.Count == 0
            && report.InvalidBlockStates.Count == 0
            && report.UnlinkedBlockRefs.Count == 0
            && report.NotCanonicalBlockRefs.Count == 0;
        return report;
    }

    public async Task<MinecraftMaterialMergeResultDto> MergeAsync(int sourceId, int targetId, CancellationToken ct = default)
    {
        if (sourceId == targetId) throw new ArgumentException("A material can't be merged into itself.");

        await using var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;
        var source = await _db.MinecraftMaterialRefs.FirstOrDefaultAsync(m => m.Id == sourceId, ct)
            ?? throw new KeyNotFoundException($"MinecraftMaterialRef with id {sourceId} not found.");
        var target = await _db.MinecraftMaterialRefs.FirstOrDefaultAsync(m => m.Id == targetId, ct)
            ?? throw new KeyNotFoundException($"MinecraftMaterialRef with id {targetId} not found.");

        var result = new MinecraftMaterialMergeResultDto
        {
            SourceId = sourceId,
            TargetId = targetId,
            Repointed = await RepointReferencesAsync(_db, sourceId, target, ct),
        };

        _db.MinecraftMaterialRefs.Remove(source);
        await _db.SaveChangesAsync(ct);
        if (transaction != null) await transaction.CommitAsync(ct);

        _logger.LogInformation("Merged MinecraftMaterialRef {SourceId} ({SourceKey}) into {TargetId} ({TargetKey}): {Repointed}",
            sourceId, source.NamespaceKey, targetId, target.NamespaceKey,
            string.Join(", ", result.Repointed.Select(r => $"{r.Key}={r.Value}")));
        return result;
    }

    /// <summary>
    /// Points every reference to <paramref name="sourceId"/> at <paramref name="target"/> (tracked, not saved).
    /// A block state follows its new material: same properties, the target's key. Used by the merge
    /// action and by the scan's clean-up (KNG-200 D7).
    /// </summary>
    internal static async Task<Dictionary<string, int>> RepointReferencesAsync(
        KnKDbContext db, int sourceId, MinecraftMaterialRef target, CancellationToken ct)
    {
        var targetKey = MinecraftNamespaceKey.TryNormalize(target.NamespaceKey) ?? target.NamespaceKey;
        var repointed = new Dictionary<string, int>();
        foreach (var referrer in MinecraftMaterialReferences.All)
        {
            var rows = await referrer.RepointAsync(db, sourceId, target.Id, ct);
            if (rows.Count == 0) continue;
            repointed[referrer.Name] = rows.Count;

            foreach (var block in rows.OfType<MinecraftBlockRef>())
            {
                var properties = block.BlockStateString is { } state && state.Contains('[') ? state[state.IndexOf('[')..] : null;
                var canonical = MinecraftBlockState.TryCanonicalize(targetKey, properties);
                block.NamespaceKey = targetKey;
                if (canonical != null)
                {
                    block.BlockStateString = canonical.State;
                    block.StateKey = MinecraftBlockState.StateKey(canonical);
                }
            }
        }
        return repointed;
    }

    private Dictionary<string, string> StaticDisplayNames()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_staticCatalog == null) return names;
        foreach (var entry in _staticCatalog.GetAll())
        {
            var key = MinecraftNamespaceKey.TryNormalize(entry.NamespaceKey);
            if (key != null && !string.IsNullOrWhiteSpace(entry.DisplayName)) names.TryAdd(key, entry.DisplayName);
        }
        return names;
    }
}
