using System;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// A player's chosen visibility for one statistics setting (DESIGN.md §F.4). ContextKey "" is the
/// metric-level value; a non-empty ContextKey overrides it for that game context (D8). No row
/// means Nobody.
/// </summary>
public class PlayerStatVisibility
{
    public int UserId { get; set; }

    /// <summary>StatisticsCatalog setting key (e.g. "pvp_kills", "economy").</summary>
    public string SettingKey { get; set; } = null!;

    public string ContextKey { get; set; } = "";

    public StatisticVisibility Visibility { get; set; }

    public DateTime UpdatedAt { get; set; }
}
