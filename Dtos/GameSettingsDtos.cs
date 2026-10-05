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
    /// Read-only, filled in on read: 1 = considered first. Deeper in the parent hierarchy first,
    /// then higher Weight, then lower id.
    /// </summary>
    [JsonPropertyName("precedence")]
    public int Precedence { get; set; }

    /// <summary>Join broadcast for this group's members; {player}, {group}. Blank = no broadcast.</summary>
    [JsonPropertyName("joinAnnouncement")]
    public string? JoinAnnouncement { get; set; }

    /// <summary>Where this group's members join (and /spawn).</summary>
    [JsonPropertyName("joinSpawnReference")]
    public LocationReferenceDto? JoinSpawnReference { get; set; }

    /// <summary>Respawn policy for this group's members, in every world.</summary>
    [JsonPropertyName("respawnPolicy")]
    public RespawnPolicyDto? RespawnPolicy { get; set; }
}

public class GameSettingsRuntimeWorldsUpdateDto
{
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
    /// Supported values: WorldSpawn, ConfiguredReference, NearestTown, JoinSpawn (respawn where the
    /// player would join - "synced" with the join spawn, KNG-52).
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
