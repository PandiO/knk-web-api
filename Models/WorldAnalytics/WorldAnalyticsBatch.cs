using System;

namespace knkwebapi_v2.Models;

/// <summary>An ingested world-analytics batch (IMPLEMENTATION_PLAN.md §1.4): the primary key is the
/// plugin's batch id, so a retried post applies nothing. Kept WorldAnalytics:BatchRetentionDays (30).</summary>
public class WorldAnalyticsBatch
{
    public Guid BatchId { get; set; }

    public string ServerName { get; set; } = "";

    public DateTime ReceivedAt { get; set; }

    /// <summary>Start of the plugin's aggregation window (UTC); its local day is the rows' day.</summary>
    public DateTime WindowStart { get; set; }

    public int RowCount { get; set; }

    public int RejectedCount { get; set; }
}
