using System;

namespace knkwebapi_v2.Models;

/// <summary>An ingested plugin statistics batch (IMPLEMENTATION_PLAN.md §3.1): the primary key is
/// the plugin's batch id, so a spool replay of the same batch applies nothing.</summary>
public class PlayerStatBatch
{
    public Guid BatchId { get; set; }

    public string ServerName { get; set; } = "";

    public DateTime ReceivedAt { get; set; }

    public int EntryCount { get; set; }

    public int RejectedCount { get; set; }
}
