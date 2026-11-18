using System.Security.Claims;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Map;

/// <summary>Who asks for the live players (O21, O22).</summary>
public enum MapAudience
{
    Anonymous,
    LoggedIn,

    /// <summary>The viewer holds <c>knk.mode.staff</c> or <c>knk.mode.owner</c> (or <c>*</c>): the in-game rule of <c>ModeService</c>.</summary>
    MaySeeHidden,
}

/// <summary>The three audience snapshots of one tick.</summary>
public sealed record MapPlayerSnapshotSet(MapPlayersDto Anonymous, MapPlayersDto LoggedIn, MapPlayersDto MaySeeHidden,
    DateTimeOffset BuiltAt)
{
    public MapPlayersDto For(MapAudience audience) => audience switch
    {
        MapAudience.MaySeeHidden => MaySeeHidden,
        MapAudience.LoggedIn => LoggedIn,
        _ => Anonymous,
    };
}

/// <summary>
/// Overview map L4 (PandiO/knk-workspace#738, OVERVIEW_MAP_DESIGN § 5 rules 6-9, § 6.2.4, O07, O13, O21-O24, O31): the
/// live player snapshots. Once per tick (<c>Map:LivePlayers:PollIntervalMs</c>, built on the first request after the
/// tick ran out, one build at a time) it builds one snapshot per audience from <see cref="MapPlayerRegistry"/>, and
/// every request is answered from the snapshot of its audience: nothing is filtered per request (O21).
/// <list type="number">
///   <item>Anonymous and logged in: players in no mode and in no running match only (rules 7, 8). The two are built
///     separately and are the same today (O08 is superseded by O31).</item>
///   <item>May see hidden: every player, with the mode tag (<c>staff</c>, <c>owner</c>) and the minigame tag.</item>
///   <item>Every audience: one arena pin per running match, "[Siege] n players" (O24, O31).</item>
/// </list>
/// Singleton; the database is read in a scope through <see cref="IMapLivePlayerData"/>, name labels are cached per
/// player for <c>Map:LivePlayers:LabelCacheSeconds</c>.
/// </summary>
public sealed class MapPlayerSnapshots
{
    public const string ModeStaffNode = "knk.mode.staff";
    public const string ModeOwnerNode = "knk.mode.owner";

    /// <summary>The nodes that let a viewer see hidden players (O22); <c>*</c> is matched by the resolver.</summary>
    public static readonly IReadOnlyList<string> SeeHiddenNodes = new[] { ModeStaffNode, ModeOwnerNode };

    private readonly MapPlayerRegistry _registry;
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptionsMonitor<MapOptions> _options;
    private readonly ILogger<MapPlayerSnapshots> _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _build = new(1, 1);
    private readonly object _labelLock = new();
    private readonly Dictionary<string, (MapPlayerLabelDto Label, DateTimeOffset ExpiresAt)> _labels = new(StringComparer.Ordinal);
    private volatile MapPlayerSnapshotSet? _current;
    private long _builds;

    public MapPlayerSnapshots(MapPlayerRegistry registry, IServiceScopeFactory scopes, IOptionsMonitor<MapOptions> options,
        ILogger<MapPlayerSnapshots> logger, TimeProvider? time = null)
    {
        _registry = registry;
        _scopes = scopes;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>How many snapshot sets were built (tests, diagnostics).</summary>
    public long Builds => Interlocked.Read(ref _builds);

    /// <summary>The caller's audience: anonymous, logged in, or may see hidden (O22, through the 30 s read cache).</summary>
    public static async Task<MapAudience> AudienceOfAsync(ClaimsPrincipal? user, HttpContext? http,
        IPermissionResolutionService permissions, IPermissionReadCache cache)
    {
        var userId = RequirePermissionFilter.UserIdFrom(user);
        if (userId is not { } id) return MapAudience.Anonymous;
        return await cache.HoldsAnyAsync(permissions, id, SeeHiddenNodes, http)
            ? MapAudience.MaySeeHidden
            : MapAudience.LoggedIn;
    }

    /// <summary>The current snapshot of <paramref name="audience"/>, rebuilt when the tick ran out.</summary>
    public async Task<MapPlayersDto> GetAsync(MapAudience audience, CancellationToken ct = default)
    {
        return (await CurrentAsync(ct)).For(audience);
    }

    public async Task<MapPlayerSnapshotSet> CurrentAsync(CancellationToken ct = default)
    {
        var tick = TimeSpan.FromMilliseconds(_options.CurrentValue.LivePlayers.EffectivePollIntervalMs);
        var current = _current;
        if (current != null && _time.GetUtcNow() - current.BuiltAt < tick) return current;

        await _build.WaitAsync(ct);
        try
        {
            current = _current;
            if (current != null && _time.GetUtcNow() - current.BuiltAt < tick) return current;
            current = await BuildAsync(ct);
            _current = current;
            Interlocked.Increment(ref _builds);
            return current;
        }
        finally
        {
            _build.Release();
        }
    }

    private async Task<MapPlayerSnapshotSet> BuildAsync(CancellationToken ct)
    {
        var options = _options.CurrentValue.LivePlayers;
        var interval = options.EffectivePollIntervalMs;
        var now = _time.GetUtcNow();
        var live = _registry.Live();

        using var scope = _scopes.CreateScope();
        var data = scope.ServiceProvider.GetRequiredService<IMapLivePlayerData>();

        var labels = await LabelsAsync(data, live.Select(p => p.Uuid).ToList(), now, options.LabelCacheSeconds, ct);
        List<MapArenaDto> arenas;
        try
        {
            arenas = await data.ArenasAsync(live, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Building the map's arena pins failed; served without arenas this tick");
            arenas = new List<MapArenaDto>();
        }

        var ordered = live.OrderBy(p => p.Uuid, StringComparer.Ordinal).ToList();
        MapPlayersDto Snapshot(bool maySeeHidden) => new()
        {
            IntervalMs = interval,
            Players = ordered
                .Where(p => maySeeHidden || !p.HiddenFromPublic)
                .Select(p => ToDto(p, labels, maySeeHidden))
                .ToList(),
            Arenas = arenas.Select(Copy).ToList(),
        };

        return new MapPlayerSnapshotSet(Snapshot(false), Snapshot(false), Snapshot(true), now);
    }

    private async Task<Dictionary<string, MapPlayerLabelDto>> LabelsAsync(IMapLivePlayerData data, List<string> uuids,
        DateTimeOffset now, int cacheSeconds, CancellationToken ct)
    {
        var result = new Dictionary<string, MapPlayerLabelDto>(StringComparer.Ordinal);
        var missing = new List<string>();
        lock (_labelLock)
        {
            foreach (var uuid in uuids)
            {
                if (_labels.TryGetValue(uuid, out var cached) && cached.ExpiresAt > now) result[uuid] = cached.Label;
                else missing.Add(uuid);
            }
            // Drop labels of players no longer online, so the cache stays the size of the server.
            foreach (var gone in _labels.Keys.Where(k => !uuids.Contains(k)).ToList())
            {
                _labels.Remove(gone);
            }
        }
        if (missing.Count == 0) return result;

        IReadOnlyDictionary<string, MapPlayerLabelDto> loaded;
        try
        {
            loaded = await data.LabelsAsync(missing, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Reading {Count} player label(s) for the map failed; their names show plain this tick", missing.Count);
            loaded = new Dictionary<string, MapPlayerLabelDto>();
        }
        var expires = now.AddSeconds(Math.Max(0, cacheSeconds));
        lock (_labelLock)
        {
            foreach (var uuid in missing)
            {
                if (loaded.TryGetValue(uuid, out var label))
                {
                    result[uuid] = label;
                    if (cacheSeconds > 0) _labels[uuid] = (label, expires);
                }
            }
        }
        return result;
    }

    private static MapPlayerDto ToDto(MapPlayerEntry p, IReadOnlyDictionary<string, MapPlayerLabelDto> labels, bool maySeeHidden)
    {
        var label = labels.TryGetValue(p.Uuid, out var found) ? found : MapPlayerLabels.Unknown();
        return new MapPlayerDto
        {
            Uuid = p.Uuid,
            World = p.World,
            X = p.X,
            Y = p.Y,
            Z = p.Z,
            Yaw = p.Yaw,
            Label = Copy(label),
            Afk = p.Afk,
            Mode = maySeeHidden ? ModeTag(p.Mode) : null,
            Minigame = maySeeHidden && p.Minigame != null ? MapPlayerLabels.MinigameTag(p.Minigame) : null,
        };
    }

    private static string? ModeTag(MapPlayerMode mode) => mode switch
    {
        MapPlayerMode.Staff => "staff",
        MapPlayerMode.Owner => "owner",
        MapPlayerMode.Unknown => "hidden",
        _ => null,
    };

    // Each audience gets its own objects, so a later change to one snapshot can never reach another.
    private static MapPlayerLabelDto Copy(MapPlayerLabelDto l) => new()
    {
        RankTag = l.RankTag,
        Braced = l.Braced,
        BracedBold = l.BracedBold,
        Name = l.Name,
        BracketColor = l.BracketColor,
        TextColor = l.TextColor,
    };

    private static MapArenaDto Copy(MapArenaDto a) => new()
    {
        Minigame = a.Minigame,
        Label = a.Label,
        PlayerCount = a.PlayerCount,
        Pin = new MapPinDto { World = a.Pin.World, X = a.Pin.X, Y = a.Pin.Y, Z = a.Pin.Z, Source = a.Pin.Source },
    };
}

/// <summary>
/// Name labels as the in-game chat line (O23; knk-plugin <c>ChatLineFormat</c>, kept in step by
/// <c>MapPlayerLabelsTests</c>): owner and staff get <c>[OWNER]</c> / <c>[STAFF]</c> in the fixed colours unless one of
/// their groups (not the Default group) set a chat colour; everyone else gets the group colours (Default group as the
/// fallback) and the premium tier before the title.
/// </summary>
public static class MapPlayerLabels
{
    public enum Rank
    {
        Owner,
        Staff,
        Member,
    }

    // knk-plugin ColorOptions: ownerformat GOLD, ownersubjects DARK_PURPLE, staffformat DARK_AQUA, staffsubjects BLUE,
    // defaultformat DARK_GREEN, defaultsubjects GREEN.
    public const string OwnerBracket = "&6";
    public const string OwnerText = "&5";
    public const string StaffBracket = "&3";
    public const string StaffText = "&9";
    public const string MemberBracket = "&2";
    public const string MemberText = "&a";

    /// <summary>The label of a player without an account record: the name only, member colours.</summary>
    public static MapPlayerLabelDto Unknown(string? name = null) => new()
    {
        Name = string.IsNullOrWhiteSpace(name) ? "?" : name,
        BracketColor = MemberBracket,
        TextColor = MemberText,
    };

    public static MapPlayerLabelDto Build(Rank rank, string name, UserDto? user)
    {
        var title = Blank(user?.TitleName);
        switch (rank)
        {
            case Rank.Owner:
            case Rank.Staff:
                var owner = rank == Rank.Owner;
                return new MapPlayerLabelDto
                {
                    RankTag = owner ? "OWNER" : "STAFF",
                    Braced = title,
                    BracedBold = true,
                    Name = name,
                    BracketColor = GroupColor(user?.ChatSecondaryColorGroupId, user?.ChatSecondaryColor, owner ? OwnerBracket : StaffBracket),
                    TextColor = GroupColor(user?.ChatPrimaryColorGroupId, user?.ChatPrimaryColor, owner ? OwnerText : StaffText),
                };
            default:
                var tier = Blank(user?.PremiumTierName);
                return new MapPlayerLabelDto
                {
                    Braced = tier == null ? title : title == null ? tier : tier + " " + title,
                    BracedBold = false,
                    Name = name,
                    BracketColor = Blank(user?.ChatSecondaryColor) ?? MemberBracket,
                    TextColor = Blank(user?.ChatPrimaryColor) ?? MemberText,
                };
        }
    }

    /// <summary>The in-game tag of a minigame key (O31): <c>siege</c> → <c>Siege</c>.</summary>
    public static string MinigameTag(string minigame)
    {
        var key = minigame.Trim();
        if (key.Length == 0) return key;
        return key.Equals("siege", StringComparison.OrdinalIgnoreCase) ? "Siege" : char.ToUpperInvariant(key[0]) + key[1..].ToLowerInvariant();
    }

    /// <summary>An arena pin's label (O31): <c>[Siege] 12 players</c>.</summary>
    public static string ArenaLabel(string tag, int count) => $"[{tag}] {count} {(count == 1 ? "player" : "players")}";

    private static string GroupColor(int? groupId, string? codes, string fallback) =>
        groupId == null ? fallback : Blank(codes) ?? fallback;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
