using System;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// One Minecraft session of a player as reported by the plugin (DESIGN.md §F.3): starts at
/// UserDataLoadedEvent, ends at quit/server stop, or is closed by the API after
/// Statistics:SessionTimeoutMinutes without a heartbeat (a duration entry).
/// </summary>
public class PlayerStatSession
{
    public long Id { get; set; }

    /// <summary>Plugin-generated UUID; the idempotency key of start/end entries.</summary>
    public Guid SessionKey { get; set; }

    public int UserId { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime LastHeartbeatAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public PlayerSessionEndReason? EndReason { get; set; }

    public int ActiveSeconds { get; set; }

    public int AfkSeconds { get; set; }

    public string ServerName { get; set; } = "";
}
