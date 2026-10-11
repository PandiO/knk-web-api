using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// Per-player statistics facts that aren't a metric (IMPLEMENTATION_PLAN.md §1.1): the first
/// recorded session (an input of "first joined", DESIGN.md §F.3) and the owner's leaderboard
/// exclusion (used from link 5; the columns exist now to avoid a second migration).
/// </summary>
public class PlayerStatProfile
{
    public int UserId { get; set; }

    public DateTime? FirstSessionAt { get; set; }

    public bool LeaderboardExcluded { get; set; }

    public string? LeaderboardExcludedReason { get; set; }

    public int? LeaderboardExcludedByUserId { get; set; }

    public DateTime UpdatedAt { get; set; }
}
