using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Dtos;

// Player statistics contracts (KNG-34, knk-workspace docs/specs/player-statistics/
// IMPLEMENTATION_PLAN.md §3.1). Enums serialise as their names (global JsonStringEnumConverter).

/// <summary>One plugin flush: sessions, durations, counters, records and PvP kills. Idempotent by
/// <see cref="BatchId"/>.</summary>
public class StatisticsBatchDto
{
    [JsonPropertyName("batchId")]
    public Guid BatchId { get; set; }

    [JsonPropertyName("serverName")]
    public string? ServerName { get; set; }

    [JsonPropertyName("pluginVersion")]
    public string? PluginVersion { get; set; }

    [JsonPropertyName("sentAt")]
    public DateTime SentAt { get; set; }

    [JsonPropertyName("sessions")]
    public List<StatisticsSessionEntryDto>? Sessions { get; set; }

    [JsonPropertyName("durations")]
    public List<StatisticsDurationEntryDto>? Durations { get; set; }

    [JsonPropertyName("counters")]
    public List<StatisticsValueEntryDto>? Counters { get; set; }

    [JsonPropertyName("records")]
    public List<StatisticsValueEntryDto>? Records { get; set; }

    [JsonPropertyName("pvpKills")]
    public List<StatisticsPvpKillEntryDto>? PvpKills { get; set; }
}

public class StatisticsSessionEntryDto
{
    /// <summary>"start" or "end".</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("sessionKey")]
    public Guid SessionKey { get; set; }

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("at")]
    public DateTime At { get; set; }

    /// <summary>"Quit", "ServerStop" or "Kick" (end entries only).</summary>
    [JsonPropertyName("endReason")]
    public string? EndReason { get; set; }
}

public class StatisticsDurationEntryDto
{
    [JsonPropertyName("sessionKey")]
    public Guid SessionKey { get; set; }

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    /// <summary>"active_playtime" or "afk_time".</summary>
    [JsonPropertyName("metric")]
    public string? Metric { get; set; }

    [JsonPropertyName("from")]
    public DateTime From { get; set; }

    [JsonPropertyName("to")]
    public DateTime To { get; set; }
}

/// <summary>A counter (sum metric) or record (max metric) entry.</summary>
public class StatisticsValueEntryDto
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("metric")]
    public string? Metric { get; set; }

    /// <summary>Game context; "" (or omitted) for non-contextual metrics.</summary>
    [JsonPropertyName("context")]
    public string? Context { get; set; }

    [JsonPropertyName("value")]
    public decimal Value { get; set; }

    [JsonPropertyName("occurredAt")]
    public DateTime OccurredAt { get; set; }
}

public class StatisticsPvpKillEntryDto
{
    [JsonPropertyName("killerUserId")]
    public int KillerUserId { get; set; }

    [JsonPropertyName("victimUserId")]
    public int VictimUserId { get; set; }

    [JsonPropertyName("context")]
    public string? Context { get; set; }

    [JsonPropertyName("occurredAt")]
    public DateTime OccurredAt { get; set; }
}

public class StatisticsBatchResultDto
{
    [JsonPropertyName("batchId")]
    public Guid BatchId { get; set; }

    /// <summary>The batch id was already ingested; nothing was applied this time.</summary>
    [JsonPropertyName("duplicate")]
    public bool Duplicate { get; set; }

    [JsonPropertyName("accepted")]
    public int Accepted { get; set; }

    [JsonPropertyName("rejected")]
    public List<StatisticsRejectedEntryDto> Rejected { get; set; } = new();
}

public class StatisticsRejectedEntryDto
{
    /// <summary>"sessions", "durations", "counters", "records" or "pvpKills".</summary>
    [JsonPropertyName("section")]
    public string Section { get; set; } = "";

    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>A <c>StatisticsRejectionCodes</c> value.</summary>
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";
}

/// <summary>Per-entry rejection codes of POST api/statistics/batches.</summary>
public static class StatisticsRejectionCodes
{
    public const string UnknownMetric = "UnknownMetric";
    public const string NotPluginWritable = "NotPluginWritable";
    public const string InvalidContext = "InvalidContext";
    public const string OutOfRange = "OutOfRange";
    public const string TooOld = "TooOld";
    public const string InFuture = "InFuture";
    public const string UnknownSession = "UnknownSession";
    public const string InvalidInterval = "InvalidInterval";
    /// <summary>The user id (or a PvP kill's killer/victim) is not an existing user.</summary>
    public const string UnknownUser = "UnknownUser";
    /// <summary>Malformed entry: unknown session type/end reason, empty key, killer == victim.</summary>
    public const string InvalidEntry = "InvalidEntry";
}

public class StatisticsCatalogDto
{
    [JsonPropertyName("timeZone")]
    public string TimeZone { get; set; } = "";

    [JsonPropertyName("contexts")]
    public List<string> Contexts { get; set; } = new();

    [JsonPropertyName("metrics")]
    public List<StatisticsCatalogMetricDto> Metrics { get; set; } = new();

    [JsonPropertyName("settings")]
    public List<StatisticsCatalogSettingDto> Settings { get; set; } = new();

    [JsonPropertyName("groups")]
    public List<StatisticsCatalogGroupDto> Groups { get; set; } = new();
}

public class StatisticsCatalogMetricDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("settingKey")]
    public string? SettingKey { get; set; }

    [JsonPropertyName("aggregation")]
    public StatisticAggregation Aggregation { get; set; }

    [JsonPropertyName("unit")]
    public StatisticUnit Unit { get; set; }

    [JsonPropertyName("contextual")]
    public bool Contextual { get; set; }

    /// <summary>"AlwaysPublic" or "Configurable" (internal metrics are not listed).</summary>
    [JsonPropertyName("visibility")]
    public string Visibility { get; set; } = "";

    [JsonPropertyName("group")]
    public string Group { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";
}

public class StatisticsCatalogSettingDto
{
    [JsonPropertyName("settingKey")]
    public string SettingKey { get; set; } = "";

    [JsonPropertyName("group")]
    public string Group { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("contextual")]
    public bool Contextual { get; set; }
}

public class StatisticsCatalogGroupDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("settingKeys")]
    public List<string> SettingKeys { get; set; } = new();
}

/// <summary>A player's statistics as one viewer may see them (visibility applied server-side).</summary>
public class PlayerStatisticsDto
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    /// <summary>"lifetime", "day", "week" or "month".</summary>
    [JsonPropertyName("period")]
    public string Period { get; set; } = "lifetime";

    [JsonPropertyName("periodStart")]
    public DateOnly? PeriodStart { get; set; }

    [JsonPropertyName("periodEndExclusive")]
    public DateOnly? PeriodEndExclusive { get; set; }

    [JsonPropertyName("timeZone")]
    public string TimeZone { get; set; } = "";

    /// <summary>"self", "staff", "signedIn" or "anonymous".</summary>
    [JsonPropertyName("viewer")]
    public string Viewer { get; set; } = "anonymous";

    [JsonPropertyName("profile")]
    public PlayerStatisticsProfileDto Profile { get; set; } = new();

    [JsonPropertyName("metrics")]
    public List<PlayerStatisticMetricDto> Metrics { get; set; } = new();

    [JsonPropertyName("economy")]
    public PlayerStatisticsEconomyDto? Economy { get; set; }

    [JsonPropertyName("discoveries")]
    public PlayerStatisticsDiscoveriesDto? Discoveries { get; set; }
}

/// <summary>The always-public base profile. Playtime here is lifetime, whatever the period.</summary>
public class PlayerStatisticsProfileDto
{
    [JsonPropertyName("titleName")]
    public string? TitleName { get; set; }

    [JsonPropertyName("titleBracketId")]
    public int? TitleBracketId { get; set; }

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

public class PlayerStatisticMetricDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("settingKey")]
    public string? SettingKey { get; set; }

    /// <summary>Display-rounded total across contexts; null when the viewer may see only some
    /// contexts of the metric (a hidden context must not leak through the total, DESIGN.md §F.4).</summary>
    [JsonPropertyName("value")]
    public decimal? Value { get; set; }

    [JsonPropertyName("rawValue")]
    public decimal? RawValue { get; set; }

    [JsonPropertyName("unit")]
    public StatisticUnit Unit { get; set; }

    [JsonPropertyName("aggregation")]
    public StatisticAggregation Aggregation { get; set; }

    /// <summary>Per-context values the viewer may see (contextual metrics only; never for deaths
    /// to non-staff viewers).</summary>
    [JsonPropertyName("contexts")]
    public List<PlayerStatisticContextValueDto>? Contexts { get; set; }
}

public class PlayerStatisticContextValueDto
{
    [JsonPropertyName("context")]
    public string Context { get; set; } = "";

    [JsonPropertyName("value")]
    public decimal Value { get; set; }

    [JsonPropertyName("rawValue")]
    public decimal RawValue { get; set; }
}

public class PlayerStatisticsEconomyDto
{
    [JsonPropertyName("coinsEarned")]
    public long CoinsEarned { get; set; }

    [JsonPropertyName("coinsSpent")]
    public long CoinsSpent { get; set; }

    [JsonPropertyName("gemsEarned")]
    public long GemsEarned { get; set; }

    [JsonPropertyName("gemsSpent")]
    public long GemsSpent { get; set; }
}

public class PlayerStatisticsDiscoveriesDto
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("towns")]
    public int Towns { get; set; }

    [JsonPropertyName("districts")]
    public int Districts { get; set; }

    /// <summary>Structures including gate structures.</summary>
    [JsonPropertyName("structures")]
    public int Structures { get; set; }
}

public class StatisticSeriesDto
{
    [JsonPropertyName("metric")]
    public string Metric { get; set; } = "";

    /// <summary>"" = total across contexts.</summary>
    [JsonPropertyName("context")]
    public string Context { get; set; } = "";

    /// <summary>"day", "week" or "month".</summary>
    [JsonPropertyName("granularity")]
    public string Granularity { get; set; } = "day";

    [JsonPropertyName("points")]
    public List<StatisticSeriesPointDto> Points { get; set; } = new();
}

public class StatisticSeriesPointDto
{
    [JsonPropertyName("periodStart")]
    public DateOnly PeriodStart { get; set; }

    /// <summary>Display-rounded value of the period.</summary>
    [JsonPropertyName("value")]
    public decimal Value { get; set; }
}

public class TitleChangeDto
{
    [JsonPropertyName("changedAt")]
    public DateTime ChangedAt { get; set; }

    [JsonPropertyName("fromTitleName")]
    public string? FromTitleName { get; set; }

    [JsonPropertyName("toTitleName")]
    public string ToTitleName { get; set; } = "";

    [JsonPropertyName("direction")]
    public TitleChangeDirection Direction { get; set; }
}

public class DiscoveryListItemDto
{
    [JsonPropertyName("domainId")]
    public int DomainId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("domainType")]
    public string DomainType { get; set; } = "";

    [JsonPropertyName("discoveredAt")]
    public DateTime DiscoveredAt { get; set; }
}

public class StatisticsVisibilityDto
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    /// <summary>False until KNG-35: "friends" is stored but shows nothing to anyone.</summary>
    [JsonPropertyName("friendsAvailable")]
    public bool FriendsAvailable { get; set; }

    [JsonPropertyName("settings")]
    public List<StatisticsVisibilitySettingDto> Settings { get; set; } = new();
}

public class StatisticsVisibilitySettingDto
{
    [JsonPropertyName("settingKey")]
    public string SettingKey { get; set; } = "";

    [JsonPropertyName("group")]
    public string Group { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("contextual")]
    public bool Contextual { get; set; }

    /// <summary>The metric-level value (Nobody when never set).</summary>
    [JsonPropertyName("visibility")]
    public StatisticVisibility Visibility { get; set; }

    /// <summary>Contexts with an override or with data. Without an override the value shown is the
    /// inherited metric-level value (<see cref="StatisticsVisibilityContextDto.IsOverride"/> false).</summary>
    [JsonPropertyName("contexts")]
    public List<StatisticsVisibilityContextDto> Contexts { get; set; } = new();
}

public class StatisticsVisibilityContextDto
{
    [JsonPropertyName("context")]
    public string Context { get; set; } = "";

    [JsonPropertyName("visibility")]
    public StatisticVisibility Visibility { get; set; }

    [JsonPropertyName("isOverride")]
    public bool IsOverride { get; set; }
}

/// <summary>An atomic multi-setting change (≤ 64). Each change carries the value the client
/// showed; any mismatch rejects the whole update (409).</summary>
public class StatisticsVisibilityUpdateDto
{
    [JsonPropertyName("changes")]
    public List<StatisticsVisibilityChangeDto>? Changes { get; set; }
}

public class StatisticsVisibilityChangeDto
{
    [JsonPropertyName("settingKey")]
    public string? SettingKey { get; set; }

    /// <summary>"" = metric level; otherwise a context override (contextual settings only).</summary>
    [JsonPropertyName("context")]
    public string? Context { get; set; }

    [JsonPropertyName("expected")]
    public StatisticVisibility Expected { get; set; }

    [JsonPropertyName("visibility")]
    public StatisticVisibility Visibility { get; set; }
}
