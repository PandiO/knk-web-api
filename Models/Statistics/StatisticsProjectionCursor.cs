using System;

namespace knkwebapi_v2.Models;

/// <summary>How far a projector has read its source (e.g. "ledger": the last projected
/// currency_entries.Id). Advanced in the same transaction as the projected rows.</summary>
public class StatisticsProjectionCursor
{
    public string Name { get; set; } = null!;

    public long LastSourceId { get; set; }

    public DateTime UpdatedAt { get; set; }
}
