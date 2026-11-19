using System.Text.RegularExpressions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.MinecraftCatalog;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.ItemIdentity;

/// <summary>
/// Item identity (PandiO/knk-workspace#141, production DESIGN §8). Works on the DbContext directly, like the
/// catalogue services it sits next to (<see cref="MinecraftCatalogScanService"/>): every method is one small query
/// or one transaction over blueprints, materials and the count rows.
/// </summary>
public partial class ItemIdentityService : IItemIdentityService
{
    public const int MaxCountLines = 5_000;
    public const int MaxReportDays = 90;
    public const int TopUnresolvedLimit = 50;

    /// <summary>Lower-case words and underscores, e.g. <c>block_drop</c>, <c>unresolved_custom_name</c>.</summary>
    [GeneratedRegex("^[a-z][a-z0-9_]{0,31}$")]
    private static partial Regex WordPattern();

    private readonly KnKDbContext _db;
    private readonly ILogger<ItemIdentityService> _logger;
    private readonly Func<DateTime> _utcNow;

    public ItemIdentityService(KnKDbContext db, ILogger<ItemIdentityService> logger)
        : this(db, logger, () => DateTime.UtcNow)
    {
    }

    /// <param name="utcNow">The clock (tests).</param>
    public ItemIdentityService(KnKDbContext db, ILogger<ItemIdentityService> logger, Func<DateTime> utcNow)
    {
        _db = db;
        _logger = logger;
        _utcNow = utcNow;
    }

    public async Task<ItemIdentityMapDto> GetMaterialMapAsync(CancellationToken ct = default)
    {
        var category = await VanillaShadowBlueprints.FindUncuratedCategoryAsync(_db, ct);
        var categoryId = category?.Id;
        var rows = await _db.ItemBlueprints.AsNoTracking()
            .Where(b => b.VanillaMaterialMatchRefId != null && b.VanillaMaterialMatch != null)
            .Select(b => new { b.Id, b.CategoryId, Key = b.VanillaMaterialMatch!.NamespaceKey })
            .ToListAsync(ct);
        return new ItemIdentityMapDto
        {
            GeneratedAt = _utcNow(),
            Entries = rows
                .Select(r => new ItemIdentityMapEntryDto
                {
                    MaterialKey = MinecraftNamespaceKey.TryNormalize(r.Key) ?? r.Key,
                    BlueprintId = r.Id,
                    Shadow = categoryId != null && r.CategoryId == categoryId,
                })
                .OrderBy(e => e.MaterialKey, StringComparer.Ordinal)
                .ToList(),
        };
    }

    public Task<ItemShadowGenerationResultDto> GenerateShadowsAsync(CancellationToken ct = default) =>
        VanillaShadowBlueprints.GenerateAsync(_db, _logger, ct);

    public async Task<ItemIdentityMatchResultDto> SetMatchAsync(int materialRefId, int? itemBlueprintId, CancellationToken ct = default)
    {
        await using var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;
        var material = await _db.MinecraftMaterialRefs.FirstOrDefaultAsync(m => m.Id == materialRefId, ct)
            ?? throw new KeyNotFoundException($"MinecraftMaterialRef with id {materialRefId} not found.");
        var holder = await _db.ItemBlueprints.FirstOrDefaultAsync(b => b.VanillaMaterialMatchRefId == materialRefId, ct);
        ItemBlueprint? next = null;
        if (itemBlueprintId is { } id)
        {
            next = await _db.ItemBlueprints.FirstOrDefaultAsync(b => b.Id == id, ct)
                ?? throw new KeyNotFoundException($"ItemBlueprint with id {id} not found.");
        }

        var result = new ItemIdentityMatchResultDto
        {
            MaterialRefId = material.Id,
            MaterialKey = material.NamespaceKey,
            PreviousBlueprintId = holder?.Id,
            BlueprintId = next?.Id,
        };
        if (holder != null && next != null && holder.Id == next.Id)
        {
            return result;
        }

        // Clear first and save, so the unique index never sees two blueprints on one material.
        if (holder != null)
        {
            holder.VanillaMaterialMatchRefId = null;
            await _db.SaveChangesAsync(ct);
        }
        if (next != null)
        {
            result.ReleasedMaterialRefId = next.VanillaMaterialMatchRefId;
            next.VanillaMaterialMatchRefId = material.Id;
            await _db.SaveChangesAsync(ct);
        }
        if (transaction != null) await transaction.CommitAsync(ct);

        _logger.LogInformation("Item identity: vanilla match of {Material} moved from blueprint {Previous} to {Next}",
            material.NamespaceKey, holder?.Id, next?.Id);
        return result;
    }

    public async Task<int> RecordConversionCountsAsync(ItemConversionCountsDto dto, CancellationToken ct = default)
    {
        if (dto?.Counts == null) throw new ArgumentException("counts is required.");
        if (dto.Counts.Count > MaxCountLines) throw new ArgumentException($"At most {MaxCountLines} count lines per report.");

        var today = _utcNow().Date;
        var day = (dto.Day ?? today).Date;
        if (day > today || day < today.AddDays(-MaxReportDays)) throw new ArgumentException("day must be within the last 90 days.");

        var lines = new Dictionary<(string Source, string Outcome, string Key), long>();
        foreach (var line in dto.Counts)
        {
            var source = line.Source?.Trim() ?? string.Empty;
            var outcome = line.Outcome?.Trim() ?? string.Empty;
            if (!WordPattern().IsMatch(source)) throw new ArgumentException($"Invalid source '{line.Source}'.");
            if (!WordPattern().IsMatch(outcome)) throw new ArgumentException($"Invalid outcome '{line.Outcome}'.");
            var key = MinecraftNamespaceKey.TryNormalize(line.MaterialKey ?? string.Empty)
                ?? throw new ArgumentException($"Invalid materialKey '{line.MaterialKey}'.");
            if (key.Length > 191) throw new ArgumentException("materialKey is longer than 191 characters.");
            if (line.Count < 0) throw new ArgumentException("count must not be negative.");
            if (line.Count == 0) continue;
            lines[(source, outcome, key)] = lines.GetValueOrDefault((source, outcome, key)) + line.Count;
        }
        if (lines.Count == 0) return 0;

        await using var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;
        var existing = await _db.ItemConversionStats.Where(s => s.Day == day).ToListAsync(ct);
        var byKey = existing.ToDictionary(s => (s.Source, s.Outcome, s.MaterialKey));
        foreach (var ((source, outcome, key), count) in lines)
        {
            if (byKey.TryGetValue((source, outcome, key), out var row))
            {
                row.Count += count;
            }
            else
            {
                _db.ItemConversionStats.Add(new ItemConversionStat { Day = day, Source = source, Outcome = outcome, MaterialKey = key, Count = count });
            }
        }
        await _db.SaveChangesAsync(ct);
        if (transaction != null) await transaction.CommitAsync(ct);
        return lines.Count;
    }

    public async Task<ItemIdentityReportDto> GetReportAsync(int days, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, MaxReportDays);
        var category = await VanillaShadowBlueprints.FindUncuratedCategoryAsync(_db, ct);
        var materials = await _db.MinecraftMaterialRefs.AsNoTracking()
            .Select(m => new { m.Id, m.NamespaceKey, m.DisplayName, m.IsItem, m.CatalogStatus })
            .ToListAsync(ct);
        var matches = await _db.ItemBlueprints.AsNoTracking()
            .Where(b => b.VanillaMaterialMatchRefId != null)
            .Select(b => new { MaterialId = b.VanillaMaterialMatchRefId!.Value, b.CategoryId })
            .ToListAsync(ct);
        var matchByMaterial = matches.ToDictionary(m => m.MaterialId, m => m.CategoryId);
        var obtainable = materials.Where(m => VanillaShadowBlueprints.IsObtainable(m.NamespaceKey, m.IsItem, m.CatalogStatus)).ToList();

        var report = new ItemIdentityReportDto
        {
            Days = days,
            UncuratedCategoryId = category?.Id,
            ObtainableMaterials = obtainable.Count,
            MatchedMaterials = obtainable.Count(m => matchByMaterial.ContainsKey(m.Id)),
            ShadowMatches = obtainable.Count(m => matchByMaterial.TryGetValue(m.Id, out var c) && category != null && c == category.Id),
            UnmatchedMaterials = obtainable
                .Where(m => !matchByMaterial.ContainsKey(m.Id))
                .OrderBy(m => m.NamespaceKey, StringComparer.Ordinal)
                .Select(m => new ItemIdentityUnmatchedMaterialDto { Id = m.Id, NamespaceKey = m.NamespaceKey, DisplayName = m.DisplayName })
                .ToList(),
        };
        report.CuratedMatches = report.MatchedMaterials - report.ShadowMatches;

        var since = _utcNow().Date.AddDays(-(days - 1));
        var stats = await _db.ItemConversionStats.AsNoTracking().Where(s => s.Day >= since).ToListAsync(ct);
        report.Conversions = stats
            .GroupBy(s => (s.Source, s.Outcome))
            .Select(g => new ItemConversionTotalDto { Source = g.Key.Source, Outcome = g.Key.Outcome, Count = g.Sum(s => s.Count) })
            .OrderBy(t => t.Source, StringComparer.Ordinal).ThenBy(t => t.Outcome, StringComparer.Ordinal)
            .ToList();
        report.TopUnresolved = stats
            .Where(s => s.Outcome.StartsWith("unresolved", StringComparison.Ordinal))
            .GroupBy(s => (s.Outcome, s.MaterialKey))
            .Select(g => new ItemConversionTotalDto { Source = "all", Outcome = g.Key.Outcome, MaterialKey = g.Key.MaterialKey, Count = g.Sum(s => s.Count) })
            .OrderByDescending(t => t.Count).ThenBy(t => t.MaterialKey, StringComparer.Ordinal)
            .Take(TopUnresolvedLimit)
            .ToList();
        return report;
    }
}
