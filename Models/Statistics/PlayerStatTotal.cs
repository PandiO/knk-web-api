using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// Lifetime value of one statistic of one player per context (IMPLEMENTATION_PLAN.md §1.1), kept
/// alongside the daily rows so lifetime reads and leaderboards never scan history. Survives the
/// daily-row retention.
/// </summary>
public class PlayerStatTotal
{
    public int UserId { get; set; }

    public string MetricKey { get; set; } = null!;

    public string ContextKey { get; set; } = "";

    public decimal Value { get; set; }

    /// <summary>When the current value was reached (leaderboard tie-break: earlier ranks first).</summary>
    public DateTime ReachedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
