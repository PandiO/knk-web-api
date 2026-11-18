using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Realtime.Presence;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Map;

/// <summary>The database reads behind the live player snapshots (scoped; <see cref="MapPlayerSnapshots"/> calls it once per tick).</summary>
public interface IMapLivePlayerData
{
    /// <summary>Name labels by UUID (O23). A UUID without an account gets the name from presence, else is left out.</summary>
    Task<IReadOnlyDictionary<string, MapPlayerLabelDto>> LabelsAsync(IReadOnlyCollection<string> uuids, CancellationToken ct = default);

    /// <summary>One pin per running match the live players play in (O13, O24, O31).</summary>
    Task<List<MapArenaDto>> ArenasAsync(IReadOnlyList<MapPlayerEntry> live, CancellationToken ct = default);
}

/// <summary>
/// Overview map L4 (PandiO/knk-workspace#738): labels from the user's title, premium tier and group colours
/// (<see cref="IUserService.GetByUuidAsync"/>, the data behind the plugin's chat line) and the rank from
/// <c>knk.mode.owner</c> / <c>knk.mode.staff</c>; arena pins from the running siege matches.
/// </summary>
public sealed class MapLivePlayerData : IMapLivePlayerData
{
    private readonly KnKDbContext _db;
    private readonly IUserService _users;
    private readonly IPermissionResolutionService _permissions;
    private readonly IPermissionReadCache _permissionCache;
    private readonly IMapPublicService _map;
    private readonly PresenceRegistry? _presence;
    private readonly MapMarkerCache _cache;
    private readonly ILogger<MapLivePlayerData> _logger;

    public MapLivePlayerData(KnKDbContext db, IUserService users, IPermissionResolutionService permissions,
        IPermissionReadCache permissionCache, IMapPublicService map, MapMarkerCache cache, ILogger<MapLivePlayerData> logger,
        PresenceRegistry? presence = null)
    {
        _db = db;
        _users = users;
        _permissions = permissions;
        _permissionCache = permissionCache;
        _map = map;
        _cache = cache;
        _logger = logger;
        _presence = presence;
    }

    public async Task<IReadOnlyDictionary<string, MapPlayerLabelDto>> LabelsAsync(IReadOnlyCollection<string> uuids,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, MapPlayerLabelDto>(StringComparer.Ordinal);
        var presentNames = _presence?.ConnectedPlayers()
            .Where(p => !string.IsNullOrWhiteSpace(p.Player.Name))
            .GroupBy(p => p.Player.Uuid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Player.Name!, StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var uuid in uuids)
        {
            ct.ThrowIfCancellationRequested();
            var user = await _users.GetByUuidAsync(uuid);
            if (user == null)
            {
                if (presentNames.TryGetValue(uuid, out var name)) result[uuid] = MapPlayerLabels.Unknown(name);
                continue;
            }
            var rank = MapPlayerLabels.Rank.Member;
            if (await _permissionCache.HoldsAnyAsync(_permissions, user.Id, new[] { MapPlayerSnapshots.ModeOwnerNode }))
            {
                rank = MapPlayerLabels.Rank.Owner;
            }
            else if (await _permissionCache.HoldsAnyAsync(_permissions, user.Id, new[] { MapPlayerSnapshots.ModeStaffNode }))
            {
                rank = MapPlayerLabels.Rank.Staff;
            }
            result[uuid] = MapPlayerLabels.Build(rank, user.Username, user);
        }
        return result;
    }

    public async Task<List<MapArenaDto>> ArenasAsync(IReadOnlyList<MapPlayerEntry> live, CancellationToken ct = default)
    {
        var matches = live
            .Where(p => p.Minigame == "siege" && p.MatchId is > 0 and <= int.MaxValue)
            .GroupBy(p => (int)p.MatchId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
        if (matches.Count == 0) return new List<MapArenaDto>();

        var ids = matches.Keys.ToList();
        var running = await _db.SiegeMatches.AsNoTracking()
            .Where(m => ids.Contains(m.Id)
                && (m.Status == SiegeMatchStatus.Created || m.Status == SiegeMatchStatus.InProgress))
            .Select(m => new { m.Id, m.SiegeScenarioId })
            .ToListAsync(ct);
        if (running.Count == 0) return new List<MapArenaDto>();

        var scenarioIds = running.Select(m => m.SiegeScenarioId).Distinct().ToList();
        // O24: the objective gate first (objectives in their order), then any gate the scenario selected.
        var objectiveGates = await _db.SiegeObjectives.AsNoTracking()
            .Where(o => scenarioIds.Contains(o.SiegeScenarioId) && o.GateStructureId != null)
            .OrderBy(o => o.SortOrder).ThenBy(o => o.Id)
            .Select(o => new { o.SiegeScenarioId, GateId = o.GateStructureId!.Value })
            .ToListAsync(ct);
        var scenarioGates = await _db.SiegeScenarioGates.AsNoTracking()
            .Where(g => scenarioIds.Contains(g.SiegeScenarioId))
            .OrderBy(g => g.GateStructureId)
            .Select(g => new { g.SiegeScenarioId, GateId = g.GateStructureId })
            .ToListAsync(ct);

        var markers = await _map.GetMarkersAsync(ct);
        var pins = markers.Structures.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().Pin);

        var result = new List<MapArenaDto>();
        foreach (var match in running.OrderBy(m => m.Id))
        {
            var candidates = objectiveGates.Where(o => o.SiegeScenarioId == match.SiegeScenarioId).Select(o => o.GateId)
                .Concat(scenarioGates.Where(g => g.SiegeScenarioId == match.SiegeScenarioId).Select(g => g.GateId));
            MapPinDto? pin = null;
            foreach (var gateId in candidates)
            {
                if (pins.TryGetValue(gateId, out var found))
                {
                    pin = found;
                    break;
                }
            }
            if (pin == null)
            {
                // Logged once per match through the marker cache's "without pin" memo (negative ids keep it apart from domains).
                if (_cache.FirstTimeWithoutPin(-match.Id))
                {
                    _logger.LogInformation("Siege match {MatchId} has no gate with a map pin; no arena pin is shown", match.Id);
                }
                continue;
            }
            var count = matches[match.Id];
            var tag = MapPlayerLabels.MinigameTag("siege");
            result.Add(new MapArenaDto
            {
                Minigame = tag,
                Label = MapPlayerLabels.ArenaLabel(tag, count),
                PlayerCount = count,
                Pin = new MapPinDto { World = pin.World, X = pin.X, Y = pin.Y, Z = pin.Z, Source = pin.Source },
            });
        }
        return result;
    }
}
