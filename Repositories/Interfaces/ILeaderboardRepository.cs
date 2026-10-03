using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Leaderboards;

namespace knkwebapi_v2.Repositories.Interfaces
{
    /// <summary>Id, name and active flag of an account (leaderboard eligibility and display).</summary>
    public sealed record LeaderboardUserRow(int Id, string Username, bool IsActive);

    /// <summary>
    /// Leaderboard data access (KNG-34, IMPLEMENTATION_PLAN.md §1.2, §4). The snapshot job reads
    /// statistics, discoveries, visibility, exclusions and merge links in bulk and replaces the
    /// current snapshots; reads only touch snapshot tables (plus usernames).
    /// </summary>
    public interface ILeaderboardRepository
    {
        // ---- snapshot inputs ----

        /// <summary>Lifetime totals of the given metric keys (ReachedAt = when the value was reached).</summary>
        Task<List<LeaderboardInputRow>> GetTotalsAsync(IReadOnlyCollection<string> metricKeys, CancellationToken ct = default);

        /// <summary>Daily rows in [from, toExclusive) aggregated per (user, metric, context): sum for
        /// <paramref name="sumMetricKeys"/>, max (earliest day holding it) for <paramref name="maxMetricKeys"/>;
        /// ReachedAt = the rows' last update.</summary>
        Task<List<LeaderboardInputRow>> GetDailyAggregatesAsync(IReadOnlyCollection<string> sumMetricKeys,
            IReadOnlyCollection<string> maxMetricKeys, DateOnly from, DateOnly toExclusive, CancellationToken ct = default);

        Task<List<(int UserId, int DomainId, DateTime DiscoveredAt)>> GetDiscoveriesAsync(CancellationToken ct = default);

        /// <summary>Account merges (secondary → primary) from the MERGE_FORFEIT ledger rows.</summary>
        Task<List<(int SecondaryId, int PrimaryId)>> GetMergeLinksAsync(CancellationToken ct = default);

        Task<Dictionary<int, LeaderboardUserRow>> GetUsersAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default);

        /// <summary>Visibility rows of the given settings, per user.</summary>
        Task<Dictionary<int, List<PlayerStatVisibility>>> GetVisibilityAsync(IReadOnlyCollection<string> settingKeys,
            CancellationToken ct = default);

        Task<HashSet<int>> GetExcludedUserIdsAsync(CancellationToken ct = default);

        // ---- snapshots ----

        /// <summary>
        /// Stores <paramref name="snapshot"/> (with entries) as the current one of its board and period,
        /// atomically: the previous current snapshot of the same period start is deleted, one of an
        /// earlier period start (a closed week/month) is kept as not current.
        /// </summary>
        Task ReplaceCurrentAsync(LeaderboardSnapshot snapshot, CancellationToken ct = default);

        /// <summary>Deletes non-current snapshots generated before <paramref name="before"/>. Returns snapshots deleted.</summary>
        Task<int> PurgeSnapshotsBeforeAsync(DateTime before, CancellationToken ct = default);

        Task<LeaderboardSnapshot?> GetCurrentAsync(string boardKey, LeaderboardPeriod period, CancellationToken ct = default);

        /// <summary>The first <paramref name="top"/> entries by rank (then tie order).</summary>
        Task<List<LeaderboardSnapshotEntry>> GetTopEntriesAsync(long snapshotId, int top, CancellationToken ct = default);

        Task<LeaderboardSnapshotEntry?> GetEntryAsync(long snapshotId, int userId, CancellationToken ct = default);

        // ---- exclusions ----

        Task<List<PlayerStatProfile>> GetExclusionsAsync(CancellationToken ct = default);

        /// <summary>Tracked profile (null when the player has none yet).</summary>
        Task<PlayerStatProfile?> GetProfileForUpdateAsync(int userId, CancellationToken ct = default);

        void AddProfile(PlayerStatProfile profile);

        Task SaveChangesAsync(CancellationToken ct = default);
    }
}
