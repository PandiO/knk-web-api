using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Map;

/// <summary>
/// The public overview map's reads (PandiO/knk-workspace#736; OVERVIEW_MAP_DESIGN § 5 rules 1-4, § 6.2.1-6.2.3): the
/// map configuration, the marker set (pins and outlines of towns, districts and structures) and the public panel of
/// each type. Everything returned is a public allow-list DTO (<see cref="MapMarkersDto"/>, O32); nothing here checks
/// <c>Map:Enabled</c> (the controller does).
/// </summary>
public interface IMapPublicService
{
    Task<MapConfigDto> GetConfigAsync(CancellationToken ct = default);
    Task<MapMarkersDto> GetMarkersAsync(CancellationToken ct = default);
    Task<MapTownPanelDto?> GetTownAsync(int id, CancellationToken ct = default);
    Task<MapDistrictPanelDto?> GetDistrictAsync(int id, CancellationToken ct = default);
    Task<MapStructurePanelDto?> GetStructureAsync(int id, CancellationToken ct = default);
}

/// <summary>
/// Process-wide state of the public map: the last built marker set (reused for <c>Map:Markers:CacheSeconds</c>) and the
/// domains already logged as having no pin, so each is logged once per start (spec § 5 rule 3). Singleton.
/// </summary>
public sealed class MapMarkerCache
{
    private readonly object _gate = new();
    private MapMarkersDto? _markers;
    private DateTimeOffset _builtAt;
    private readonly HashSet<int> _loggedWithoutPin = new();

    public MapMarkersDto? Get(DateTimeOffset now, TimeSpan maxAge)
    {
        lock (_gate)
        {
            return _markers != null && maxAge > TimeSpan.Zero && now - _builtAt < maxAge ? _markers : null;
        }
    }

    public void Set(MapMarkersDto markers, DateTimeOffset now)
    {
        lock (_gate)
        {
            _markers = markers;
            _builtAt = now;
        }
    }

    /// <summary>True the first time a domain id is seen without a pin.</summary>
    public bool FirstTimeWithoutPin(int domainId)
    {
        lock (_gate)
        {
            return _loggedWithoutPin.Add(domainId);
        }
    }
}

/// <summary>Plane geometry for pins (O18).</summary>
public static class MapGeometry
{
    /// <summary>
    /// The area centroid of a closed polygon of <c>(x, z)</c> points; the mean of the points when the polygon has no
    /// area (fewer than three points, or all on one line). Null for no points.
    /// </summary>
    public static (double X, double Z)? Centroid(IReadOnlyList<(int X, int Z)> points)
    {
        if (points.Count == 0) return null;
        double area2 = 0, cx = 0, cz = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var (x0, z0) = points[i];
            var (x1, z1) = points[(i + 1) % points.Count];
            var cross = (double)x0 * z1 - (double)x1 * z0;
            area2 += cross;
            cx += (x0 + x1) * cross;
            cz += (z0 + z1) * cross;
        }
        if (Math.Abs(area2) < 1e-9)
        {
            return (points.Average(p => (double)p.X), points.Average(p => (double)p.Z));
        }
        return (cx / (3 * area2), cz / (3 * area2));
    }
}

public sealed class MapPublicService : IMapPublicService
{
    public const string StructureType = "Structure";
    public const string GateSubtype = "Gate";

    private readonly KnKDbContext _db;
    private readonly IOptionsMonitor<MapOptions> _options;
    private readonly MapMarkerCache _cache;
    private readonly ILogger<MapPublicService> _logger;
    private readonly TimeProvider _time;

    public MapPublicService(KnKDbContext db, IOptionsMonitor<MapOptions> options, MapMarkerCache cache,
        ILogger<MapPublicService> logger, TimeProvider? time = null)
    {
        _db = db;
        _options = options;
        _cache = cache;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public async Task<MapConfigDto> GetConfigAsync(CancellationToken ct = default)
    {
        var o = _options.CurrentValue;
        var markers = await GetMarkersAsync(ct);
        return new MapConfigDto
        {
            Enabled = o.Enabled,
            MarkerVersion = markers.Version,
            Labels = new MapLabelDistancesDto
            {
                Town = Distance(o.Labels.Town),
                District = Distance(o.Labels.District),
                Structure = Distance(o.Labels.Structure),
                Arena = Distance(o.Labels.Arena),
            },
            Flash = new MapFlashDto
            {
                Count = Math.Clamp(o.Flash.Count, 1, 10),
                DurationMs = Math.Clamp(o.Flash.DurationMs, 50, 5000),
            },
            // Phases 4a (#738) and 6a (#741) wire their own flags (O27); until then both parts are off.
            LivePlayers = new MapLivePlayersConfigDto { Enabled = false, PollIntervalMs = 2000 },
            GateCommands = new MapPartConfigDto { Enabled = false },
        };
    }

    private static double? Distance(MapLabelDistance? d) => d == null || d.MaxDistance <= 0 ? null : d.MaxDistance;

    public async Task<MapMarkersDto> GetMarkersAsync(CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        var maxAge = TimeSpan.FromSeconds(Math.Max(0, _options.CurrentValue.Markers.CacheSeconds));
        var cached = _cache.Get(now, maxAge);
        if (cached != null) return cached;

        var markers = await BuildMarkersAsync(ct);
        _cache.Set(markers, now);
        return markers;
    }

    private sealed record DomainRow(int Id, string Name, string? WgRegionId, string? WorldName,
        double? LocX, double? LocY, double? LocZ, string? LocWorld);

    private async Task<MapMarkersDto> BuildMarkersAsync(CancellationToken ct)
    {
        var towns = await _db.Towns.AsNoTracking()
            .OrderBy(t => t.Id)
            .Select(t => new { Row = new DomainRow(t.Id, t.Name, t.WgRegionId, t.WorldName,
                t.Location != null ? t.Location.X : null, t.Location != null ? t.Location.Y : null,
                t.Location != null ? t.Location.Z : null, t.Location != null ? t.Location.World : null) })
            .ToListAsync(ct);
        var districts = await _db.Districts.AsNoTracking()
            .OrderBy(d => d.Id)
            .Select(d => new { d.TownId, Row = new DomainRow(d.Id, d.Name, d.WgRegionId, d.WorldName,
                d.Location != null ? d.Location.X : null, d.Location != null ? d.Location.Y : null,
                d.Location != null ? d.Location.Z : null, d.Location != null ? d.Location.World : null) })
            .ToListAsync(ct);
        var structures = await _db.Structures.AsNoTracking()
            .OrderBy(s => s.Id)
            .Select(s => new { s.DistrictId, TownId = s.District != null ? s.District.TownId : 0,
                Row = new DomainRow(s.Id, s.Name, s.WgRegionId, s.WorldName,
                s.Location != null ? s.Location.X : null, s.Location != null ? s.Location.Y : null,
                s.Location != null ? s.Location.Z : null, s.Location != null ? s.Location.World : null) })
            .ToListAsync(ct);
        var gateIds = (await _db.GateStructures.AsNoTracking().Select(g => g.Id).ToListAsync(ct)).ToHashSet();
        var subRegions = (await _db.DomainSubRegions.AsNoTracking()
                .Select(s => new { s.DomainId, s.WgRegionId }).ToListAsync(ct))
            .GroupBy(s => s.DomainId)
            .ToDictionary(g => g.Key, g => g.Select(s => (s.WgRegionId ?? "").ToLowerInvariant()).ToHashSet());
        var outlines = await _db.MapRegionOutlines.AsNoTracking()
            .OrderBy(o => o.WorldName).ThenBy(o => o.WgRegionId)
            .Select(o => new { o.WgRegionId, o.WorldName, o.PointsJson, o.MinY, o.MaxY, o.DomainId })
            .ToListAsync(ct);
        var outlinesByRegion = outlines.ToLookup(o => o.WgRegionId.ToLowerInvariant());
        var outlinesByDomain = outlines.Where(o => o.DomainId.HasValue).ToLookup(o => o.DomainId!.Value);

        // A domain's shape (L1 notes): its main region row (the domain's own WgRegionId is the authority, in its world
        // when it has one) plus the rows of its current sub-regions that L1 linked to it. Rows linked to a domain that
        // no longer names them are stale and ignored.
        (MapPinDto? Pin, List<MapOutlineDto> Outlines) Shape(DomainRow d)
        {
            var main = string.IsNullOrWhiteSpace(d.WgRegionId)
                ? null
                : outlinesByRegion[d.WgRegionId.Trim().ToLowerInvariant()]
                    .Where(o => d.WorldName == null || string.Equals(o.WorldName, d.WorldName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(o => o.DomainId == d.Id)
                    .FirstOrDefault();
            var subs = subRegions.TryGetValue(d.Id, out var names)
                ? outlinesByDomain[d.Id].Where(o => names.Contains(o.WgRegionId.ToLowerInvariant()) && o != main).ToList()
                : new();

            var list = new List<MapOutlineDto>();
            var mainPoints = Array.Empty<(int, int)>() as IReadOnlyList<(int X, int Z)>;
            foreach (var o in (main == null ? subs : subs.Prepend(main)))
            {
                var points = MapOutlinePoints.Parse(o.PointsJson);
                if (points.Count < 3) continue;
                if (o == main) mainPoints = points;
                list.Add(new MapOutlineDto
                {
                    World = o.WorldName,
                    Points = points.Select(p => new[] { p.X, p.Z }).ToList(),
                    MinY = o.MinY,
                    MaxY = o.MaxY,
                });
            }

            MapPinDto? pin = null;
            if (d.LocX.HasValue && d.LocZ.HasValue)
            {
                pin = new MapPinDto
                {
                    World = d.LocWorld ?? d.WorldName ?? main?.WorldName ?? "world",
                    X = d.LocX.Value,
                    Y = d.LocY,
                    Z = d.LocZ.Value,
                    Source = "location",
                };
            }
            else
            {
                // The centroid of the main region; a domain whose main outline is missing uses its first sub-region.
                var basis = mainPoints.Count >= 3 ? mainPoints : list.Count > 0
                    ? list[0].Points.Select(p => (p[0], p[1])).ToList() : null;
                var c = basis == null ? null : MapGeometry.Centroid(basis);
                if (c.HasValue)
                {
                    pin = new MapPinDto
                    {
                        World = list[0].World,
                        X = Math.Round(c.Value.X + 0.5, 1),
                        Y = null,
                        Z = Math.Round(c.Value.Z + 0.5, 1),
                        Source = "centroid",
                    };
                }
            }

            if (pin == null && _cache.FirstTimeWithoutPin(d.Id))
            {
                _logger.LogInformation("Overview map: domain {DomainId} ({Name}) has no spawn location and no region outline; it gets no pin",
                    d.Id, d.Name);
            }
            return (pin, list);
        }

        var result = new MapMarkersDto();
        foreach (var t in towns)
        {
            var (pin, list) = Shape(t.Row);
            if (pin == null) continue;
            result.Towns.Add(new MapTownMarkerDto { Id = t.Row.Id, Name = t.Row.Name, Pin = pin, Outlines = list });
        }
        foreach (var d in districts)
        {
            var (pin, list) = Shape(d.Row);
            if (pin == null) continue;
            result.Districts.Add(new MapDistrictMarkerDto { Id = d.Row.Id, TownId = d.TownId, Name = d.Row.Name, Pin = pin, Outlines = list });
        }
        foreach (var s in structures)
        {
            var (pin, list) = Shape(s.Row);
            if (pin == null) continue;
            var subtype = gateIds.Contains(s.Row.Id) ? GateSubtype : null;
            result.Structures.Add(new MapStructureMarkerDto
            {
                Id = s.Row.Id,
                DistrictId = s.DistrictId,
                TownId = s.TownId,
                Name = s.Row.Name,
                Type = StructureType,
                Subtype = subtype,
                TypeLabel = subtype == null ? StructureType : $"{StructureType}, {subtype}",
                Pin = pin,
                Outlines = list,
            });
        }

        result.Version = Version(result);
        return result;
    }

    /// <summary>A short hash of the marker content: it changes exactly when a pin, label or outline changes.</summary>
    private static string Version(MapMarkersDto markers)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new { markers.Towns, markers.Districts, markers.Structures });
        return Convert.ToHexString(SHA256.HashData(json), 0, 8).ToLowerInvariant();
    }

    public async Task<MapTownPanelDto?> GetTownAsync(int id, CancellationToken ct = default) =>
        await _db.Towns.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new MapTownPanelDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description ?? string.Empty,
                DistrictCount = t.Districts.Count(),
            })
            .FirstOrDefaultAsync(ct);

    public async Task<MapDistrictPanelDto?> GetDistrictAsync(int id, CancellationToken ct = default) =>
        await _db.Districts.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new MapDistrictPanelDto
            {
                Id = d.Id,
                Name = d.Name,
                Description = d.Description ?? string.Empty,
                TownName = d.Town != null ? d.Town.Name : string.Empty,
            })
            .FirstOrDefaultAsync(ct);

    public async Task<MapStructurePanelDto?> GetStructureAsync(int id, CancellationToken ct = default)
    {
        var panel = await _db.Structures.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new MapStructurePanelDto
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description ?? string.Empty,
                Type = StructureType,
                DistrictName = s.District != null ? s.District.Name : string.Empty,
                TownName = s.District != null && s.District.Town != null ? s.District.Town.Name : string.Empty,
                StreetName = s.Street != null ? s.Street.Name : string.Empty,
                HouseNumber = s.HouseNumber,
            })
            .FirstOrDefaultAsync(ct);
        if (panel == null) return null;

        var gate = await _db.GateStructures.AsNoTracking()
            .Include(g => g.GateDoors)
            .FirstOrDefaultAsync(g => g.Id == id, ct);
        if (gate == null) return panel;

        var underSiege = gate.CurrentSiegeId.HasValue && await _db.SiegeMatches.AsNoTracking()
            .AnyAsync(m => m.Id == gate.CurrentSiegeId.Value
                && (m.Status == SiegeMatchStatus.Created || m.Status == SiegeMatchStatus.InProgress), ct);

        panel.Subtype = GateSubtype;
        panel.Gate = new MapGatePanelDto
        {
            UnderSiege = underSiege,
            Doors = gate.GateDoors
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Id)
                .Select(d => new MapGateDoorPanelDto
                {
                    Name = d.Name,
                    OpenState = GateDoor.GetEffective(d, gate, x => x.OpenedState, g => g.OpenedStateOverride)
                        .ToString().ToLowerInvariant(),
                    IsDestroyed = GateDoor.GetEffective(d, gate, x => x.IsDestroyed, g => g.IsDestroyedOverride),
                    HealthCurrent = d.HealthCurrent,
                    HealthMax = d.HealthMax,
                })
                .ToList(),
        };
        return panel;
    }
}
