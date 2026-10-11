using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// One statistic of one player on one local day (KNG-34, docs/specs/player-statistics/
/// IMPLEMENTATION_PLAN.md §1.1). Weekly and monthly views are sums (or maxima) of these rows.
/// Written by ingestion (plugin facts) and the projectors (ledger, Siege); never by anything else.
/// </summary>
public class PlayerStatDaily
{
    public int UserId { get; set; }

    /// <summary>Local day in Statistics:TimeZone (StatisticsPeriods.LocalDay).</summary>
    public DateOnly Day { get; set; }

    /// <summary>StatisticsCatalog metric key.</summary>
    public string MetricKey { get; set; } = null!;

    /// <summary>Game context ("open_world", "siege", …); "" for non-contextual metrics.</summary>
    public string ContextKey { get; set; } = "";

    /// <summary>Sum or maximum per the metric's aggregation; precise (display rounding happens on read).</summary>
    public decimal Value { get; set; }

    /// <summary>Last time the value changed (for max metrics: when the current maximum was reached).</summary>
    public DateTime UpdatedAt { get; set; }
}
