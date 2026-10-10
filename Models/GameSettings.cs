using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// Singleton configuration entity for global gameplay and world-specific server settings.
/// </summary>
public class GameSettings
{
    public string Id { get; set; } = "global";

    public string SettingsVersion { get; set; } = "1";

    public string JoinAnnouncement { get; set; } = "&a{player} joined the server.";

    public string LeaveAnnouncement { get; set; } = "&e{player} left the server.";

    /// <summary>
    /// Supported values: WorldSpawn, CustomReference.
    /// </summary>
    public string JoinSpawnMode { get; set; } = "WorldSpawn";

    /// <summary>
    /// Serialized LocationReferenceDto.
    /// </summary>
    public string? JoinSpawnReferenceJson { get; set; }

    /// <summary>
    /// Serialized RespawnPolicyDto for global fallback behavior.
    /// </summary>
    public string? DefaultRespawnPolicyJson { get; set; }

    /// <summary>
    /// Serialized List&lt;WorldGameSettingsDto&gt;.
    /// </summary>
    public string WorldSettingsJson { get; set; } = "[]";

    /// <summary>
    /// Serialized List&lt;MinecraftWorldRuntimeDto&gt; updated by the plugin.
    /// </summary>
    public string RuntimeWorldsJson { get; set; } = "[]";

    public DateTime? RuntimeWorldsLastUpdatedAt { get; set; }

    /// <summary>
    /// Server-list MOTD ("&amp;"-coded, up to two lines). Null or blank = the server's own
    /// server.properties motd (KNG-52).
    /// </summary>
    public string? Motd { get; set; }

    /// <summary>
    /// Serialized List&lt;PermissionGroupGameSettingsDto&gt;: per-group join message, join spawn and
    /// respawn overrides (KNG-52, docs/specs/game-settings/DESIGN.md §3.8).
    /// </summary>
    public string GroupOverridesJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
