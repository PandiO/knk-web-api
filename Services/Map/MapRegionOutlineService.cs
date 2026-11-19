using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Realtime.Contracts;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Map;

/// <summary>What one <c>ReportRegionOutlines</c> changed.</summary>
public sealed record RegionOutlineReportResult(int Stored, int Unchanged, int Removed, IReadOnlyList<string> Rejected);

public interface IMapRegionOutlineService
{
    /// <summary>
    /// Stores (inserts or replaces) the reported outlines by world and region id, deletes the <c>Removed</c> ones, and links
    /// each stored row to the domain that names the region. Invalid entries are skipped and listed in the result.
    /// </summary>
    Task<RegionOutlineReportResult> ApplyReportAsync(RegionOutlineReportDto report, CancellationToken cancellationToken = default);
}

/// <summary>
/// Overview map L1 (PandiO/knk-workspace#735, O15, O28): keeps <c>map_region_outlines</c> in line with what the game
/// server reports. Only the game hub calls it (plugin key on the hub connection).
/// </summary>
public sealed class MapRegionOutlineService : IMapRegionOutlineService
{
    /// <summary>More regions than any world has; a larger report is cut and the rest listed as rejected.</summary>
    public const int MaxOutlinesPerReport = 5000;

    /// <summary>A polygon region with more points than this is not stored (a drawn cylinder has 32-64).</summary>
    public const int MaxPointsPerOutline = 2048;

    public const int MaxRegionIdLength = 128;
    public const int MaxWorldNameLength = 64;

    private readonly KnKDbContext _db;
    private readonly TimeProvider _time;
    private readonly ILogger<MapRegionOutlineService> _logger;

    public MapRegionOutlineService(KnKDbContext db, ILogger<MapRegionOutlineService> logger, TimeProvider? time = null)
    {
        _db = db;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<RegionOutlineReportResult> ApplyReportAsync(RegionOutlineReportDto report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        var rejected = new List<string>();
        var now = _time.GetUtcNow().UtcDateTime;

        // 1. Validate and normalise; the last entry for a region wins.
        var valid = new Dictionary<(string World, string Region), RegionOutlineDto>();
        var outlines = report.Outlines ?? new List<RegionOutlineDto>();
        if (outlines.Count > MaxOutlinesPerReport)
        {
            rejected.Add($"{outlines.Count - MaxOutlinesPerReport} outline(s) over the limit of {MaxOutlinesPerReport} per report");
            outlines = outlines.Take(MaxOutlinesPerReport).ToList();
        }
        foreach (var outline in outlines)
        {
            if (outline == null) continue;
            var reason = Validate(outline);
            var key = Key(outline.WorldName, outline.WgRegionId);
            if (reason != null)
            {
                rejected.Add($"{key.Region} ({key.World}): {reason}");
                continue;
            }
            valid[key] = outline;
        }
        var removedKeys = (report.Removed ?? new List<RegionOutlineRefDto>())
            .Where(r => r != null && !string.IsNullOrWhiteSpace(r.WgRegionId) && !string.IsNullOrWhiteSpace(r.WorldName))
            .Select(r => Key(r.WorldName, r.WgRegionId))
            // a region renamed onto itself (redefined in place) is stored, not removed
            .Where(k => !valid.ContainsKey(k))
            .ToHashSet();

        // 2. Read the rows and domains involved once.
        var regionIds = valid.Keys.Select(k => k.Region).Concat(removedKeys.Select(k => k.Region)).Distinct().ToList();
        var existing = regionIds.Count == 0
            ? new List<MapRegionOutline>()
            : await _db.MapRegionOutlines.Where(o => regionIds.Contains(o.WgRegionId)).ToListAsync(cancellationToken);
        var existingByKey = existing.GroupBy(o => Key(o.WorldName, o.WgRegionId)).ToDictionary(g => g.Key, g => g.First());
        var domainLinks = await ResolveDomainsAsync(valid.Keys.ToList(), cancellationToken);

        // 3. Apply.
        int stored = 0, unchanged = 0, removed = 0;
        foreach (var (key, outline) in valid)
        {
            var points = MapOutlinePoints.Serialize(outline.Points!.Select(p => (p.X, p.Z)));
            var minY = Math.Min(outline.MinY, outline.MaxY);
            var maxY = Math.Max(outline.MinY, outline.MaxY);
            domainLinks.TryGetValue(key, out var domainId);
            if (existingByKey.TryGetValue(key, out var row))
            {
                if (row.PointsJson == points && row.MinY == minY && row.MaxY == maxY && row.DomainId == domainId)
                {
                    unchanged++;
                    continue;
                }
                row.PointsJson = points;
                row.MinY = minY;
                row.MaxY = maxY;
                row.DomainId = domainId;
                row.UpdatedAt = now;
            }
            else
            {
                _db.MapRegionOutlines.Add(new MapRegionOutline
                {
                    WgRegionId = key.Region,
                    WorldName = key.World,
                    PointsJson = points,
                    MinY = minY,
                    MaxY = maxY,
                    DomainId = domainId,
                    UpdatedAt = now,
                });
            }
            stored++;
        }
        foreach (var key in removedKeys)
        {
            if (existingByKey.TryGetValue(key, out var row))
            {
                _db.MapRegionOutlines.Remove(row);
                removed++;
            }
        }
        if (stored > 0 || removed > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        return new RegionOutlineReportResult(stored, unchanged, removed, rejected);
    }

    private static string? Validate(RegionOutlineDto outline)
    {
        if (string.IsNullOrWhiteSpace(outline.WgRegionId)) return "no region id";
        if (outline.WgRegionId.Trim().Length > MaxRegionIdLength) return $"region id longer than {MaxRegionIdLength}";
        if (string.IsNullOrWhiteSpace(outline.WorldName)) return "no world";
        if (outline.WorldName.Trim().Length > MaxWorldNameLength) return $"world name longer than {MaxWorldNameLength}";
        var count = outline.Points?.Count(p => p != null) ?? 0;
        if (count < 3) return "fewer than three points";
        if (count > MaxPointsPerOutline) return $"more than {MaxPointsPerOutline} points";
        return null;
    }

    /// <summary>World names keep their case (Bukkit's are case-sensitive); region ids are lower case, as in WorldGuard.</summary>
    private static (string World, string Region) Key(string? world, string? regionId) =>
        ((world ?? string.Empty).Trim(), (regionId ?? string.Empty).Trim().ToLowerInvariant());

    /// <summary>
    /// The domain that names each region: its main region (<c>Domain.WgRegionId</c>) or one of its sub-regions, in the
    /// region's world (a domain without a world yet matches any world, as in KNG-111).
    /// </summary>
    private async Task<Dictionary<(string World, string Region), int?>> ResolveDomainsAsync(
        IReadOnlyCollection<(string World, string Region)> keys, CancellationToken cancellationToken)
    {
        var result = new Dictionary<(string World, string Region), int?>();
        if (keys.Count == 0) return result;
        var ids = keys.Select(k => k.Region).Distinct().ToList();
        var mains = await _db.Domains.AsNoTracking()
            .Where(d => ids.Contains(d.WgRegionId))
            .Select(d => new { d.Id, d.WgRegionId, d.WorldName })
            .ToListAsync(cancellationToken);
        var subs = await _db.DomainSubRegions.AsNoTracking()
            .Where(s => ids.Contains(s.WgRegionId))
            .Select(s => new { s.DomainId, s.WgRegionId, s.Domain.WorldName })
            .ToListAsync(cancellationToken);
        foreach (var key in keys)
        {
            bool Matches(string regionId, string? world) =>
                string.Equals(regionId?.Trim(), key.Region, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(world) || string.Equals(world.Trim(), key.World, StringComparison.Ordinal));
            var candidates = mains.Where(d => Matches(d.WgRegionId, d.WorldName)).Select(d => (d.Id, d.WorldName))
                .Concat(subs.Where(s => Matches(s.WgRegionId, s.WorldName)).Select(s => (Id: s.DomainId, s.WorldName)))
                .ToList();
            // a domain in the exact world wins over one that has no world yet
            var match = candidates.OrderBy(c => string.IsNullOrWhiteSpace(c.WorldName) ? 1 : 0).ThenBy(c => c.Id)
                .Select(c => (int?)c.Id).FirstOrDefault();
            if (candidates.Select(c => c.Id).Distinct().Count() > 1)
            {
                _logger.LogWarning("Region {Region} in {World} is named by several domains ({Domains}); linked to {Chosen}",
                    key.Region, key.World, string.Join(", ", candidates.Select(c => c.Id).Distinct()), match);
            }
            result[key] = match;
        }
        return result;
    }
}
