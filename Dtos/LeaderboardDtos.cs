using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Dtos;

// Leaderboard contracts (KNG-34, knk-workspace docs/specs/player-statistics/IMPLEMENTATION_PLAN.md
// §3.2). Enums serialise as their names (global JsonStringEnumConverter).

/// <summary>One board of <c>GET api/leaderboards</c>.</summary>
public class LeaderboardBoardDto
{
    [JsonPropertyName("boardKey")]
    public string BoardKey { get; set; } = "";

    [JsonPropertyName("metric")]
    public string Metric { get; set; } = "";

    [JsonPropertyName("context")]
    public string? Context { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("unit")]
    public StatisticUnit Unit { get; set; }

    /// <summary>"weekly", "monthly", "lifetime".</summary>
    [JsonPropertyName("periods")]
    public List<string> Periods { get; set; } = new();

    /// <summary>
    /// True when the metric is always public (e.g. active playtime): anyone may read the board.
    /// Other boards rank only players who set the metric to Everyone and need a signed-in viewer
    /// (L1-3: "everyone" = any signed-in viewer).
    /// </summary>
    [JsonPropertyName("alwaysPublic")]
    public bool AlwaysPublic { get; set; }
}

/// <summary>A board's current snapshot for one period, top N plus the viewer's own row.</summary>
public class LeaderboardViewDto
{
    [JsonPropertyName("boardKey")]
    public string BoardKey { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("unit")]
    public StatisticUnit Unit { get; set; }

    [JsonPropertyName("period")]
    public string Period { get; set; } = "";

    /// <summary>First local day of the week/month; null for lifetime.</summary>
    [JsonPropertyName("periodStart")]
    public DateOnly? PeriodStart { get; set; }

    /// <summary>When the snapshot was built; null when no snapshot exists yet.</summary>
    [JsonPropertyName("generatedAt")]
    public DateTime? GeneratedAt { get; set; }

    [JsonPropertyName("totalRanked")]
    public int TotalRanked { get; set; }

    [JsonPropertyName("entries")]
    public List<LeaderboardRankEntryDto> Entries { get; set; } = new();

    /// <summary>The viewer's own position; null for anonymous viewers or when not ranked.</summary>
    [JsonPropertyName("viewer")]
    public LeaderboardViewerEntryDto? Viewer { get; set; }
}

public class LeaderboardRankEntryDto
{
    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    /// <summary>Display-rounded (StatisticsFormatting).</summary>
    [JsonPropertyName("value")]
    public decimal Value { get; set; }

    [JsonPropertyName("rawValue")]
    public decimal RawValue { get; set; }
}

public class LeaderboardViewerEntryDto
{
    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("value")]
    public decimal Value { get; set; }

    [JsonPropertyName("rawValue")]
    public decimal RawValue { get; set; }
}

/// <summary>An owner exclusion (DESIGN.md §F.11).</summary>
public class LeaderboardExclusionDto
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("excludedByUserId")]
    public int? ExcludedByUserId { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

public class LeaderboardExclusionRequestDto
{
    /// <summary>Why (≤ 200 characters; shown to owners only).</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}
