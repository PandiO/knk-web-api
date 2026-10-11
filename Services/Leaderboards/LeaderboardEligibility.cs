using System;
using System.Collections.Generic;
using System.Linq;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Statistics;

namespace knkwebapi_v2.Services.Leaderboards
{
    /// <summary>A stored statistic value of one identity, with when it was reached.</summary>
    public sealed record LeaderboardInputRow(int UserId, string MetricKey, string ContextKey, decimal Value, DateTime ReachedAt);

    /// <summary>One player's board value before ranking.</summary>
    public sealed record LeaderboardCandidate(int UserId, decimal Value, DateTime ReachedAt);

    /// <summary>A ranked player (competition rank).</summary>
    public sealed record LeaderboardRankedEntry(int Rank, int UserId, decimal Value, DateTime ReachedAt);

    /// <summary>
    /// Pure leaderboard rules (KNG-34, DESIGN.md §F.11, §F.4):
    /// <list type="bullet">
    /// <item>eligibility — always-public metrics always rank; configurable ones only when effectively
    /// Everyone (per context for a per-context board, the total rule for a total board; Friends fails
    /// closed); excluded and inactive accounts never rank;</item>
    /// <item>merged identities — secondary accounts count toward their final primary;</item>
    /// <item>board values — sum or max across the board's contexts and the player's identities;</item>
    /// <item>ranking — value descending, competition ranks (1, 1, 3), ties ordered by earlier
    /// reachedAt (then user id for a stable order); zero values don't rank.</item>
    /// </list>
    /// </summary>
    public static class LeaderboardEligibility
    {
        /// <summary>Whether a player with these visibility rows may appear on the board.</summary>
        public static bool IsEligible(LeaderboardBoardDefinition board, IReadOnlyCollection<PlayerStatVisibility> visibility)
        {
            if (board.AlwaysPublic) return true;
            if (board.SettingKey == null) return false;
            return StatisticsVisibilityRules.IsPublic(visibility, board.SettingKey, board.Context);
        }

        /// <summary>
        /// Final primary per account: follows secondary → primary merge links transitively (cycles
        /// stop at the first repeat). Accounts without a link map to themselves.
        /// </summary>
        public static Func<int, int> PrimaryResolver(IReadOnlyCollection<(int SecondaryId, int PrimaryId)> links)
        {
            var parent = new Dictionary<int, int>();
            foreach (var (secondary, primary) in links)
            {
                if (secondary != primary) parent[secondary] = primary;
            }
            var cache = new Dictionary<int, int>();
            return userId =>
            {
                if (cache.TryGetValue(userId, out var known)) return known;
                var seen = new HashSet<int> { userId };
                var current = userId;
                while (parent.TryGetValue(current, out var next) && seen.Add(next))
                {
                    current = next;
                }
                cache[userId] = current;
                return current;
            };
        }

        /// <summary>
        /// Board values per primary from stored rows: rows of the board's source metric in the board's
        /// context (all contexts for a total board), summed or maxed per the metric's aggregation.
        /// reachedAt: the latest contribution for sums; for max, the earliest row holding the maximum.
        /// </summary>
        public static List<LeaderboardCandidate> Values(LeaderboardBoardDefinition board, IEnumerable<LeaderboardInputRow> rows,
            Func<int, int> primaryOf)
        {
            var result = new Dictionary<int, (decimal Value, DateTime At)>();
            foreach (var row in rows)
            {
                if (board.Context != null && row.ContextKey != board.Context) continue;
                if (row.MetricKey != LeaderboardCatalog.SourceMetricKey(board.Metric, row.ContextKey)) continue;
                var userId = primaryOf(row.UserId);
                if (!result.TryGetValue(userId, out var current))
                {
                    result[userId] = (row.Value, row.ReachedAt);
                }
                else if (board.Aggregation == StatisticAggregation.Sum)
                {
                    result[userId] = (current.Value + row.Value, row.ReachedAt > current.At ? row.ReachedAt : current.At);
                }
                else if (row.Value > current.Value || (row.Value == current.Value && row.ReachedAt < current.At))
                {
                    result[userId] = (row.Value, row.ReachedAt);
                }
            }
            return result.Select(r => new LeaderboardCandidate(r.Key, r.Value.Value, r.Value.At)).ToList();
        }

        /// <summary>
        /// Discovery counts per primary: each domain once per player (earliest discovery across the
        /// merged identities, as the statistics read does — L2-8), counted when that discovery lies in
        /// [<paramref name="fromUtc"/>, <paramref name="toUtc"/>) (null = no bound). reachedAt = the
        /// latest counted discovery.
        /// </summary>
        public static List<LeaderboardCandidate> DiscoveryValues(
            IEnumerable<(int UserId, int DomainId, DateTime DiscoveredAt)> discoveries, Func<int, int> primaryOf,
            DateTime? fromUtc, DateTime? toUtc)
        {
            return discoveries
                .GroupBy(d => (Primary: primaryOf(d.UserId), d.DomainId))
                .Select(g => (g.Key.Primary, At: g.Min(d => d.DiscoveredAt)))
                .Where(d => (fromUtc == null || d.At >= fromUtc) && (toUtc == null || d.At < toUtc))
                .GroupBy(d => d.Primary)
                .Select(g => new LeaderboardCandidate(g.Key, g.Count(), g.Max(d => d.At)))
                .ToList();
        }

        /// <summary>Competition ranking of the positive values (1, 1, 3), ties by earlier reachedAt.</summary>
        public static List<LeaderboardRankedEntry> Rank(IEnumerable<LeaderboardCandidate> candidates)
        {
            var ordered = candidates.Where(c => c.Value > 0)
                .OrderByDescending(c => c.Value)
                .ThenBy(c => c.ReachedAt)
                .ThenBy(c => c.UserId)
                .ToList();
            var ranked = new List<LeaderboardRankedEntry>(ordered.Count);
            for (var i = 0; i < ordered.Count; i++)
            {
                var rank = i > 0 && ordered[i].Value == ordered[i - 1].Value ? ranked[i - 1].Rank : i + 1;
                ranked.Add(new LeaderboardRankedEntry(rank, ordered[i].UserId, ordered[i].Value, ordered[i].ReachedAt));
            }
            return ranked;
        }
    }
}
