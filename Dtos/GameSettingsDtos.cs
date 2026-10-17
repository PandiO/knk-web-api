using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos;

public class GameSettingsReadDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "global";

    [JsonPropertyName("settingsVersion")]
    public string SettingsVersion { get; set; } = "1";

    [JsonPropertyName("joinAnnouncement")]
    public string JoinAnnouncement { get; set; } = "&a{player} joined the server.";

    [JsonPropertyName("leaveAnnouncement")]
    public string LeaveAnnouncement { get; set; } = "&e{player} left the server.";

    [JsonPropertyName("joinSpawnMode")]
    public string JoinSpawnMode { get; set; } = "WorldSpawn";

    [JsonPropertyName("joinSpawnReference")]
    public LocationReferenceDto? JoinSpawnReference { get; set; }

    [JsonPropertyName("defaultRespawnPolicy")]
    public RespawnPolicyDto? DefaultRespawnPolicy { get; set; }

    [JsonPropertyName("worldSettings")]
    public List<WorldGameSettingsDto> WorldSettings { get; set; } = new();

    [JsonPropertyName("runtimeWorlds")]
    public List<MinecraftWorldRuntimeDto> RuntimeWorlds { get; set; } = new();

    [JsonPropertyName("runtimeWorldsLastUpdatedAt")]
    public DateTime? RuntimeWorldsLastUpdatedAt { get; set; }

    /// <summary>Server-list MOTD; null = the server's own.</summary>
    [JsonPropertyName("motd")]
    public string? Motd { get; set; }

    /// <summary>Per-group overrides, in precedence order (the first that applies to a player wins).</summary>
    [JsonPropertyName("groupOverrides")]
    public List<PermissionGroupGameSettingsDto> GroupOverrides { get; set; } = new();

    /// <summary>The hub (hub DESIGN §16 Phase 4): never null on read; no hub domain = no hub.</summary>
    [JsonPropertyName("hub")]
    public HubSettingsDto Hub { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

public class GameSettingsUpdateDto
{
    [JsonPropertyName("settingsVersion")]
    public string SettingsVersion { get; set; } = "1";

    [JsonPropertyName("joinAnnouncement")]
    public string JoinAnnouncement { get; set; } = "&a{player} joined the server.";

    [JsonPropertyName("leaveAnnouncement")]
    public string LeaveAnnouncement { get; set; } = "&e{player} left the server.";

    [JsonPropertyName("joinSpawnMode")]
    public string JoinSpawnMode { get; set; } = "WorldSpawn";

    [JsonPropertyName("joinSpawnReference")]
    public LocationReferenceDto? JoinSpawnReference { get; set; }

    [JsonPropertyName("defaultRespawnPolicy")]
    public RespawnPolicyDto? DefaultRespawnPolicy { get; set; }

    [JsonPropertyName("worldSettings")]
    public List<WorldGameSettingsDto> WorldSettings { get; set; } = new();

    /// <summary>Null = keep the current MOTD; blank = clear it (use server.properties).</summary>
    [JsonPropertyName("motd")]
    public string? Motd { get; set; }

    /// <summary>Null = keep the current overrides; a list (also empty) replaces them.</summary>
    [JsonPropertyName("groupOverrides")]
    public List<PermissionGroupGameSettingsDto>? GroupOverrides { get; set; }

    /// <summary>Null = keep the current hub settings.</summary>
    [JsonPropertyName("hub")]
    public HubSettingsDto? Hub { get; set; }
}

/// <summary>
/// The hub (hub DESIGN §2, §13, §16.4): where every joining player is placed, what happens to online players when the
/// API is lost, and the rules inside the hub. No <see cref="DomainId"/> = no hub: players join as before.
/// </summary>
public class HubSettingsDto
{
    /// <summary>The selected hub: a Domain flagged <c>IsHub</c> (decision 4). Null = no hub.</summary>
    [JsonPropertyName("domainId")]
    public int? DomainId { get; set; }

    /// <summary>Read-only, filled in on read: the hub domain's name, type and world.</summary>
    [JsonPropertyName("domainName")]
    public string? DomainName { get; set; }

    [JsonPropertyName("domainType")]
    public string? DomainType { get; set; }

    [JsonPropertyName("worldName")]
    public string? WorldName { get; set; }

    /// <summary>Read-only, filled in on read: the hub domain's WorldGuard region (its rules apply inside it).</summary>
    [JsonPropertyName("wgRegionId")]
    public string? WgRegionId { get; set; }

    /// <summary>Where joining players are placed: required with a hub, in the hub domain's world.</summary>
    [JsonPropertyName("spawnReference")]
    public LocationReferenceDto? SpawnReference { get; set; }

    /// <summary>
    /// What happens to online players when the API is lost: None, SendToHub (default), Kick, SendToWorldSpawn
    /// (the world they are in at that moment, §15).
    /// </summary>
    [JsonPropertyName("outageAction")]
    public string OutageAction { get; set; } = "SendToHub";

    /// <summary>Seconds between detecting the outage and the action; 0 = "Uit", immediately (§10, §13).</summary>
    [JsonPropertyName("outageDelaySeconds")]
    public int OutageDelaySeconds { get; set; }

    [JsonPropertyName("rules")]
    public HubRulesDto Rules { get; set; } = new();

    /// <summary>
    /// The hub's portals (§11, developer decision 2026-10-12): WorldGuard region ids in the hub's world. Walking into
    /// one takes the player to the gameplay world through the Game Settings join chain (group spawn, else the default
    /// spawn), once the API and their account are ready.
    /// </summary>
    [JsonPropertyName("portalRegionIds")]
    public List<string> PortalRegionIds { get; set; } = new();
}

/// <summary>
/// The hub's rules (§13), applied inside the selected hub's region only. Defaults are the confirmed initial values;
/// healing and feeding default to vanilla 1.21.10 Peaceful (§16.5).
/// </summary>
public class HubRulesDto
{
    [JsonPropertyName("damagePlayers")]
    public bool DamagePlayers { get; set; }

    [JsonPropertyName("damageEntities")]
    public bool DamageEntities { get; set; }

    [JsonPropertyName("takeDamage")]
    public bool TakeDamage { get; set; }

    [JsonPropertyName("buildBreak")]
    public bool BuildBreak { get; set; }

    [JsonPropertyName("dropPickup")]
    public bool DropPickup { get; set; } = true;

    [JsonPropertyName("useDoors")]
    public bool UseDoors { get; set; } = true;

    [JsonPropertyName("useNpcs")]
    public bool UseNpcs { get; set; } = true;

    /// <summary>Chests, barrels, shulker boxes and other block storage.</summary>
    [JsonPropertyName("useStorage")]
    public bool UseStorage { get; set; }

    [JsonPropertyName("useItemFramesPaintings")]
    public bool UseItemFramesPaintings { get; set; }

    /// <summary>Health regained per heal; 0 = no hub healing.</summary>
    [JsonPropertyName("healAmount")]
    public double HealAmount { get; set; } = 1.0;

    [JsonPropertyName("healIntervalTicks")]
    public int HealIntervalTicks { get; set; } = 20;

    /// <summary>Food level gained per feed; 0 = no hub feeding.</summary>
    [JsonPropertyName("feedAmount")]
    public int FeedAmount { get; set; } = 1;

    [JsonPropertyName("feedIntervalTicks")]
    public int FeedIntervalTicks { get; set; } = 10;

    /// <summary>Saturation gained per interval, up to 20.</summary>
    [JsonPropertyName("saturationAmount")]
    public double SaturationAmount { get; set; } = 1.0;

    [JsonPropertyName("saturationIntervalTicks")]
    public int SaturationIntervalTicks { get; set; } = 20;
}

/// <summary>
/// One PermissionGroup's overrides of the global Game Settings (KNG-52). Every field is optional:
/// null means "use the next group, else the global/world setting".
/// </summary>
public class PermissionGroupGameSettingsDto
{
    [JsonPropertyName("permissionGroupId")]
    public int PermissionGroupId { get; set; }

    /// <summary>Read-only: the group's name, filled in on read.</summary>
    [JsonPropertyName("groupName")]
    public string? GroupName { get; set; }

    /// <summary>
    /// Read-only, filled in on read: 1 = considered first. The order teleport fees use (KNG-41):
    /// higher Weight first, each group followed by its parent chain.
    /// </summary>
    [JsonPropertyName("precedence")]
    public int Precedence { get; set; }

    /// <summary>Join broadcast for this group's members; {player}, {group}, {title}. Blank = no broadcast.</summary>
    [JsonPropertyName("joinAnnouncement")]
    public string? JoinAnnouncement { get; set; }

    /// <summary>Quit broadcast for this group's members; same placeholders. Blank = no broadcast.</summary>
    [JsonPropertyName("leaveAnnouncement")]
    public string? LeaveAnnouncement { get; set; }

    /// <summary>Where this group's members join (and /spawn).</summary>
    [JsonPropertyName("joinSpawnReference")]
    public LocationReferenceDto? JoinSpawnReference { get; set; }

    /// <summary>
    /// True: this group's members join where they logged out (no join teleport, like owners); /spawn
    /// still takes them to the server spawn. Replaces <see cref="JoinSpawnReference"/> (round 4).
    /// </summary>
    [JsonPropertyName("joinAtLastLocation")]
    public bool? JoinAtLastLocation { get; set; }

    /// <summary>Respawn policy for this group's members, in every world.</summary>
    [JsonPropertyName("respawnPolicy")]
    public RespawnPolicyDto? RespawnPolicy { get; set; }

    /// <summary>The hub's outage action for this group's members (null = the next group, else the hub's).</summary>
    [JsonPropertyName("outageAction")]
    public string? OutageAction { get; set; }

    /// <summary>The outage delay in seconds for this group's members; 0 = immediately (null = not set).</summary>
    [JsonPropertyName("outageDelaySeconds")]
    public int? OutageDelaySeconds { get; set; }

}

public class GameSettingsRuntimeWorldsUpdateDto
{
    /// <summary>
    /// KNG-118 (A9): the reporting game server's id (the plugin's <c>game-settings.server-id</c>). A report
    /// replaces only that server's worlds, so a second server doesn't wipe the first's list. Null or blank =
    /// <see cref="MinecraftWorldRuntimeDto.DefaultServerId"/> (plugins before KNG-118).
    /// </summary>
    [JsonPropertyName("serverId")]
    public string? ServerId { get; set; }

    [JsonPropertyName("runtimeWorlds")]
    public List<MinecraftWorldRuntimeDto> RuntimeWorlds { get; set; } = new();
}

public class MinecraftWorldRuntimeDto
{
    [JsonPropertyName("worldName")]
    public string WorldName { get; set; } = string.Empty;

    [JsonPropertyName("folderName")]
    public string FolderName { get; set; } = string.Empty;

    [JsonPropertyName("environment")]
    public string Environment { get; set; } = string.Empty;

    [JsonPropertyName("loaded")]
    public bool Loaded { get; set; } = true;

    [JsonPropertyName("playerCount")]
    public int PlayerCount { get; set; }

    [JsonPropertyName("isPrimary")]
    public bool IsPrimary { get; set; }

    /// <summary>The server id used for reports without one (and for entries stored before KNG-118).</summary>
    public const string DefaultServerId = "default";

    /// <summary>KNG-118 (A9), read-only: the game server that reported this world (set by the API).</summary>
    [JsonPropertyName("serverId")]
    public string? ServerId { get; set; }

    /// <summary>KNG-118 (A9), read-only: when that server last reported it (set by the API, UTC).</summary>
    [JsonPropertyName("reportedAt")]
    public DateTime? ReportedAt { get; set; }
}

public class WorldGameSettingsDto
{
    [JsonPropertyName("worldName")]
    public string WorldName { get; set; } = string.Empty;

    [JsonPropertyName("worldFolderName")]
    public string? WorldFolderName { get; set; }

    [JsonPropertyName("defaultGameMode")]
    public string DefaultGameMode { get; set; } = "SURVIVAL";

    [JsonPropertyName("lockTime")]
    public bool LockTime { get; set; }

    [JsonPropertyName("lockedTime")]
    public long LockedTime { get; set; } = 18000;

    [JsonPropertyName("weather")]
    public WorldWeatherSettingsDto Weather { get; set; } = new();

    [JsonPropertyName("worldSpawnReference")]
    public LocationReferenceDto? WorldSpawnReference { get; set; }

    [JsonPropertyName("respawnPolicy")]
    public RespawnPolicyDto RespawnPolicy { get; set; } = new();
}

public class WorldWeatherSettingsDto
{
    /// <summary>
    /// Supported values: Normal, Constant, Blocked, Weighted.
    /// </summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "Normal";

    /// <summary>
    /// Supported values: CLEAR, RAIN, THUNDER.
    /// </summary>
    [JsonPropertyName("forcedWeather")]
    public string? ForcedWeather { get; set; }

    [JsonPropertyName("blockedWeatherTypes")]
    public List<string> BlockedWeatherTypes { get; set; } = new();

    [JsonPropertyName("clearWeight")]
    public int ClearWeight { get; set; } = 34;

    [JsonPropertyName("rainWeight")]
    public int RainWeight { get; set; } = 33;

    [JsonPropertyName("thunderWeight")]
    public int ThunderWeight { get; set; } = 33;
}

public class RespawnPolicyDto
{
    /// <summary>
    /// Supported values: WorldSpawn (forced, beds ignored), ConfiguredReference, NearestTown, JoinSpawn
    /// (respawn where the player would join - "synced" with the join spawn, KNG-52), ServerDefault (not
    /// redirected: bed or respawn anchor, else the world spawn - what staff and owners get; round 4).
    /// </summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "WorldSpawn";

    [JsonPropertyName("locationReference")]
    public LocationReferenceDto? LocationReference { get; set; }

    [JsonPropertyName("maxNearestTownDistance")]
    public double? MaxNearestTownDistance { get; set; }

    [JsonPropertyName("useWorldSpawnFallback")]
    public bool UseWorldSpawnFallback { get; set; } = true;
}

public class LocationReferenceDto
{
    /// <summary>
    /// Supported values: Location, Town, District, Structure.
    /// </summary>
    [JsonPropertyName("sourceType")]
    public string SourceType { get; set; } = "Location";

    [JsonPropertyName("sourceId")]
    public int SourceId { get; set; }

    [JsonPropertyName("displayLabel")]
    public string DisplayLabel { get; set; } = string.Empty;

    [JsonPropertyName("location")]
    public LocationSnapshotDto? Location { get; set; }
}

public class LocationSnapshotDto
{
    [JsonPropertyName("locationId")]
    public int? LocationId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }

    [JsonPropertyName("yaw")]
    public float Yaw { get; set; }

    [JsonPropertyName("pitch")]
    public float Pitch { get; set; }

    [JsonPropertyName("world")]
    public string World { get; set; } = "world";
}

/// <summary>A domain flagged hub-eligible (decision 4), as the hub block lists them.</summary>
public class HubDomainDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("domainType")]
    public string DomainType { get; set; } = string.Empty;

    [JsonPropertyName("worldName")]
    public string? WorldName { get; set; }

    [JsonPropertyName("wgRegionId")]
    public string? WgRegionId { get; set; }

    [JsonPropertyName("isHub")]
    public bool IsHub { get; set; }

    /// <summary>Whether it is the hub selected in Game Settings.</summary>
    [JsonPropertyName("selected")]
    public bool Selected { get; set; }
}

public class HubEligibilityUpdateDto
{
    [JsonPropertyName("isHub")]
    public bool IsHub { get; set; }
}
