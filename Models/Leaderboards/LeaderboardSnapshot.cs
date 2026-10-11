using System;
using System.Collections.Generic;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// One precomputed ranking of a board for a period (KNG-34, IMPLEMENTATION_PLAN.md §1.2). Reads
/// are served from the current snapshot only, never from raw history (DESIGN.md §F.11). The
/// final snapshot of a closed week/month is kept (IsCurrent = false) until retention.
/// </summary>
public class LeaderboardSnapshot
{
    public long Id { get; set; }

    /// <summary><c>&lt;metric&gt;</c> or <c>&lt;metric&gt;@&lt;context&gt;</c>.</summary>
    public string BoardKey { get; set; } = null!;

    public LeaderboardPeriod Period { get; set; }

    /// <summary>First local day of the week/month; null for lifetime.</summary>
    public DateOnly? PeriodStart { get; set; }

    public DateTime GeneratedAt { get; set; }

    public bool IsCurrent { get; set; }

    /// <summary>Players ranked (may exceed the stored entries, capped at Leaderboards:MaxEntriesPerBoard).</summary>
    public int EntryCount { get; set; }

    public List<LeaderboardSnapshotEntry> Entries { get; set; } = new();
}
