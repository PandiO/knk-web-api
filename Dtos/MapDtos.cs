using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos;

// Public overview map DTOs (PandiO/knk-workspace#736; OVERVIEW_MAP_DESIGN § 5 rules 1-4, § 6.2.1-6.2.3, O16, O32).
// These are served to anonymous callers. They are an allow-list: a field is added here only through code review
// against O32, and MapPublicDtoFieldTests pins the exact JSON property set of every type. Never add region ids,
// owner data, permissions or other internal ids.

/// <summary><c>GET api/Map/config</c> (spec § 6.2.1).</summary>
public class MapConfigDto
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>Changes whenever the marker set changes; the page reloads markers when it differs (spec § 5 rule 2).</summary>
    [JsonPropertyName("markerVersion")]
    public string MarkerVersion { get; set; } = string.Empty;

    [JsonPropertyName("labels")]
    public MapLabelDistancesDto Labels { get; set; } = new();

    [JsonPropertyName("flash")]
    public MapFlashDto Flash { get; set; } = new();

    /// <summary>Live players (phase 4a, #738, <c>Map:LivePlayers:Enabled</c>); always off until that phase wires it.</summary>
    [JsonPropertyName("livePlayers")]
    public MapLivePlayersConfigDto LivePlayers { get; set; } = new();

    /// <summary>Gate commands (phase 6a, #741): follows <c>Map:GateCommands:Enabled</c>.</summary>
    [JsonPropertyName("gateCommands")]
    public MapPartConfigDto GateCommands { get; set; } = new();
}

/// <summary>Label distances per pin kind in blocks of BlueMap camera distance; null means the label shows at every zoom (O19).</summary>
public class MapLabelDistancesDto
{
    [JsonPropertyName("town")]
    public double? Town { get; set; }

    [JsonPropertyName("district")]
    public double? District { get; set; }

    [JsonPropertyName("structure")]
    public double? Structure { get; set; }

    [JsonPropertyName("arena")]
    public double? Arena { get; set; }
}

public class MapFlashDto
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("durationMs")]
    public int DurationMs { get; set; }
}

public class MapPartConfigDto
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

public class MapLivePlayersConfigDto : MapPartConfigDto
{
    /// <summary>How often the page polls <c>api/Map/players</c> (O21, default 2 s).</summary>
    [JsonPropertyName("pollIntervalMs")]
    public int PollIntervalMs { get; set; } = 2000;
}

/// <summary><c>GET api/Map/markers</c> (spec § 6.2.2): every town, district and structure that has a pin.</summary>
public class MapMarkersDto
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("towns")]
    public List<MapTownMarkerDto> Towns { get; set; } = new();

    [JsonPropertyName("districts")]
    public List<MapDistrictMarkerDto> Districts { get; set; } = new();

    [JsonPropertyName("structures")]
    public List<MapStructureMarkerDto> Structures { get; set; } = new();
}

/// <summary>Where a pin sits (O18): the domain's spawn location, otherwise the 2D centroid of its main outline.</summary>
public class MapPinDto
{
    [JsonPropertyName("world")]
    public string World { get; set; } = string.Empty;

    [JsonPropertyName("x")]
    public double X { get; set; }

    /// <summary>The spawn location's Y; null for a centroid pin (an outline has no single height: the bridge picks one).</summary>
    [JsonPropertyName("y")]
    public double? Y { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }

    /// <summary><c>location</c> or <c>centroid</c>.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// One region outline: <c>[[x, z], ...]</c> in block coordinates as the game server reported them (each point is the
/// block at that corner, inclusive; draw the line through the block centres, +0.5), with the region's height range.
/// </summary>
public class MapOutlineDto
{
    [JsonPropertyName("world")]
    public string World { get; set; } = string.Empty;

    [JsonPropertyName("points")]
    public List<int[]> Points { get; set; } = new();

    [JsonPropertyName("minY")]
    public int MinY { get; set; }

    [JsonPropertyName("maxY")]
    public int MaxY { get; set; }
}

public class MapTownMarkerDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>The label: <c>Domain.Name</c> (O20), never the region id.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("pin")]
    public MapPinDto Pin { get; set; } = new();

    /// <summary>The main region first, then the sub-regions; empty for a domain pinned by its location only.</summary>
    [JsonPropertyName("outlines")]
    public List<MapOutlineDto> Outlines { get; set; } = new();
}

public class MapDistrictMarkerDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>The town the district belongs to, so the town toggle can show its districts (O05).</summary>
    [JsonPropertyName("townId")]
    public int TownId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("pin")]
    public MapPinDto Pin { get; set; } = new();

    [JsonPropertyName("outlines")]
    public List<MapOutlineDto> Outlines { get; set; } = new();
}

public class MapStructureMarkerDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("districtId")]
    public int DistrictId { get; set; }

    [JsonPropertyName("townId")]
    public int TownId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Always <c>Structure</c>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary><c>Gate</c> for a gate structure, null for a plain structure.</summary>
    [JsonPropertyName("subtype")]
    public string? Subtype { get; set; }

    /// <summary>Type and subtype for the pin label, for example <c>Structure, Gate</c> (O06, spec § 5 rule 3).</summary>
    [JsonPropertyName("typeLabel")]
    public string TypeLabel { get; set; } = string.Empty;

    [JsonPropertyName("pin")]
    public MapPinDto Pin { get; set; } = new();

    [JsonPropertyName("outlines")]
    public List<MapOutlineDto> Outlines { get; set; } = new();
}

/// <summary><c>GET api/Map/towns/{id}</c>, the public town panel (O32: name, description, number of districts).</summary>
public class MapTownPanelDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("districtCount")]
    public int DistrictCount { get; set; }
}

/// <summary><c>GET api/Map/districts/{id}</c>, the public district panel (O32: name, description, town).</summary>
public class MapDistrictPanelDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("townName")]
    public string TownName { get; set; } = string.Empty;
}

/// <summary>
/// <c>GET api/Map/structures/{id}</c>, the public structure panel (O32: name, description, type and subtype, district
/// and town, street and house number; a gate adds its doors and "under siege").
/// </summary>
public class MapStructurePanelDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("subtype")]
    public string? Subtype { get; set; }

    [JsonPropertyName("districtName")]
    public string DistrictName { get; set; } = string.Empty;

    [JsonPropertyName("townName")]
    public string TownName { get; set; } = string.Empty;

    [JsonPropertyName("streetName")]
    public string StreetName { get; set; } = string.Empty;

    [JsonPropertyName("houseNumber")]
    public int HouseNumber { get; set; }

    /// <summary>Only for a gate structure; null otherwise.</summary>
    [JsonPropertyName("gate")]
    public MapGatePanelDto? Gate { get; set; }
}

public class MapGatePanelDto
{
    /// <summary>True while a siege match that locked this gate down is created or in progress.</summary>
    [JsonPropertyName("underSiege")]
    public bool UnderSiege { get; set; }

    [JsonPropertyName("doors")]
    public List<MapGateDoorPanelDto> Doors { get; set; } = new();
}

/// <summary>One gate door (O32: name, open/closed, destroyed, health; health shown always, also during a siege).</summary>
public class MapGateDoorPanelDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The effective state (structure override applied): <c>open</c>, <c>closed</c>, <c>opening</c>, <c>closing</c> or <c>jammed</c>.</summary>
    [JsonPropertyName("openState")]
    public string OpenState { get; set; } = string.Empty;

    /// <summary>Effective (structure override applied).</summary>
    [JsonPropertyName("isDestroyed")]
    public bool IsDestroyed { get; set; }

    [JsonPropertyName("healthCurrent")]
    public double HealthCurrent { get; set; }

    [JsonPropertyName("healthMax")]
    public double HealthMax { get; set; }
}

/// <summary>
/// <c>GET api/Map/players</c> (PandiO/knk-workspace#738, spec § 6.2.4, § 5 rules 6-9, O07, O13, O21-O24, O31): the
/// caller's audience snapshot. Anonymous and plain logged-in callers never receive a player in staff or owner mode or in
/// a running match, not even in this body; a viewer who may see hidden players gets them with their tags.
/// </summary>
public class MapPlayersDto
{
    /// <summary>How often the page should poll, in milliseconds (<c>Map:LivePlayers:PollIntervalMs</c>).</summary>
    [JsonPropertyName("intervalMs")]
    public int IntervalMs { get; set; }

    [JsonPropertyName("players")]
    public List<MapPlayerDto> Players { get; set; } = new();

    /// <summary>One pin per running minigame match (O13, O24, O31), the same for every audience.</summary>
    [JsonPropertyName("arenas")]
    public List<MapArenaDto> Arenas { get; set; } = new();
}

/// <summary>
/// One live player (O07): head (by UUID) and name label, position and tags. The UUID is the public Minecraft account
/// id the head is drawn from; no account id, email or other user field is served.
/// </summary>
public class MapPlayerDto
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("world")]
    public string World { get; set; } = string.Empty;

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }

    /// <summary>Facing in whole degrees, Minecraft convention (0 = south, 90 = west).</summary>
    [JsonPropertyName("yaw")]
    public int Yaw { get; set; }

    [JsonPropertyName("label")]
    public MapPlayerLabelDto Label { get; set; } = new();

    /// <summary>The AFK tag (O07).</summary>
    [JsonPropertyName("afk")]
    public bool Afk { get; set; }

    /// <summary>
    /// The mode tag: <c>staff</c> or <c>owner</c> (an unknown mode reads <c>hidden</c>); null outside a mode. Only ever
    /// set in the "may see hidden" snapshot.
    /// </summary>
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    /// <summary>The short minigame tag (<c>Siege</c>) of a player in a running match; only in the "may see hidden" snapshot.</summary>
    [JsonPropertyName("minigame")]
    public string? Minigame { get; set; }
}

/// <summary>
/// A player's name label as their in-game chat line (O23, <c>ChatLineFormat</c>): <c>[OWNER]</c> / <c>[STAFF]</c> for
/// holders of <c>knk.mode.owner</c> / <c>knk.mode.staff</c>, then <c>-{title}-</c> (members: premium tier and title),
/// then the name. Colours are Minecraft <c>&amp;</c> codes (one colour, optionally formats or a hex colour); the web
/// app turns them into CSS.
/// </summary>
public class MapPlayerLabelDto
{
    /// <summary><c>OWNER</c>, <c>STAFF</c> or null.</summary>
    [JsonPropertyName("rankTag")]
    public string? RankTag { get; set; }

    /// <summary>The text inside <c>-{ }-</c>, or null to leave the segment out.</summary>
    [JsonPropertyName("braced")]
    public string? Braced { get; set; }

    /// <summary>The braced text is bold (owner and staff, as in game).</summary>
    [JsonPropertyName("bracedBold")]
    public bool BracedBold { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Colour codes of the brackets (<c>[ ]</c>, <c>-{ }-</c>).</summary>
    [JsonPropertyName("bracketColor")]
    public string BracketColor { get; set; } = string.Empty;

    /// <summary>Colour codes of the rank tag, the braced text and the name.</summary>
    [JsonPropertyName("textColor")]
    public string TextColor { get; set; } = string.Empty;
}

/// <summary>A running minigame match's pin (O24, O31): at its objective, with the in-game tag and the participant count.</summary>
public class MapArenaDto
{
    /// <summary>The minigame tag, e.g. <c>Siege</c>.</summary>
    [JsonPropertyName("minigame")]
    public string Minigame { get; set; } = string.Empty;

    /// <summary>The pin label, e.g. <c>[Siege] 12 players</c>.</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("playerCount")]
    public int PlayerCount { get; set; }

    [JsonPropertyName("pin")]
    public MapPinDto Pin { get; set; } = new();
}
