using System;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos;

/// <summary>
/// The always-public part of a player's profile (KNG-34, IMPLEMENTATION_PLAN.md §3.2,
/// DESIGN.md §F.1 "always public"): served to anyone, signed in or not. Deliberately no online
/// flag, last-seen time, email or UUID — vanish must not leak. Everything else comes from
/// <c>GET api/statistics/users/{userId}</c>, filtered for the viewer.
/// </summary>
public class PublicPlayerProfileDto
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("titleName")]
    public string? TitleName { get; set; }

    [JsonPropertyName("experience")]
    public int Experience { get; set; }

    [JsonPropertyName("coins")]
    public int Coins { get; set; }

    [JsonPropertyName("gems")]
    public int Gems { get; set; }

    [JsonPropertyName("firstJoinedAt")]
    public DateTime? FirstJoinedAt { get; set; }

    [JsonPropertyName("activePlaytimeSeconds")]
    public long ActivePlaytimeSeconds { get; set; }

    [JsonPropertyName("afkSeconds")]
    public long AfkSeconds { get; set; }
}
