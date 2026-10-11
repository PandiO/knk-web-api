using System;

namespace knkwebapi_v2.Models;

/// <summary>One ranked player of a <see cref="LeaderboardSnapshot"/> (competition rank: 1, 1, 3).</summary>
public class LeaderboardSnapshotEntry
{
    public long SnapshotId { get; set; }

    public int Rank { get; set; }

    /// <summary>The surviving (primary) account; merged identities are counted into it.</summary>
    public int UserId { get; set; }

    public decimal Value { get; set; }

    /// <summary>When the value was reached — the tie order (earlier first).</summary>
    public DateTime ReachedAt { get; set; }

    public LeaderboardSnapshot Snapshot { get; set; } = null!;
}
