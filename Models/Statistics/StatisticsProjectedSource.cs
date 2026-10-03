using System;

namespace knkwebapi_v2.Models;

/// <summary>A source record that has been projected once (e.g. SourceType "siege_match", the
/// match id). Inserted in the same transaction as the projected rows, so a match is never
/// counted twice.</summary>
public class StatisticsProjectedSource
{
    public string SourceType { get; set; } = null!;

    public long SourceId { get; set; }

    public DateTime ProjectedAt { get; set; }
}
