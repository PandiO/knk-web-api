using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Statistics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Leaderboards
{
    /// <summary>
    /// Builds the current snapshot of every board × period (KNG-34, IMPLEMENTATION_PLAN.md §4,
    /// DESIGN.md §F.11): weekly/monthly from the daily rows of the current local week/month, lifetime
    /// from the totals, discoveries from user_domain_discoveries; merged secondary identities count
    /// toward their primary; eligibility (visibility, exclusions, active accounts) applied per board;
    /// competition ranks; the snapshot replaces the current one atomically. Scoped (one DbContext).
    /// </summary>
    public class LeaderboardSnapshotBuilder
    {
        private readonly ILeaderboardRepository _repo;
        private readonly LeaderboardsOptions _options;
        private readonly StatisticsOptions _statistics;
        private readonly TimeProvider _time;
        private readonly ILogger<LeaderboardSnapshotBuilder> _logger;

        public LeaderboardSnapshotBuilder(ILeaderboardRepository repo, IOptions<LeaderboardsOptions>? options = null,
            IOptions<StatisticsOptions>? statistics = null, TimeProvider? time = null, ILogger<LeaderboardSnapshotBuilder>? logger = null)
        {
            _repo = repo;
            _options = options?.Value ?? new LeaderboardsOptions();
            _statistics = statistics?.Value ?? new StatisticsOptions();
            _time = time ?? TimeProvider.System;
            _logger = logger ?? NullLogger<LeaderboardSnapshotBuilder>.Instance;
        }

        /// <summary>Rebuilds every board for every period and purges old closed-period snapshots.
        /// Returns the number of snapshots written.</summary>
        public async Task<int> BuildAllAsync(CancellationToken ct = default)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var zone = StatisticsPeriods.FindZone(_statistics.TimeZone);
            var today = StatisticsPeriods.LocalDay(now, zone);

            var primaryOf = LeaderboardEligibility.PrimaryResolver(await _repo.GetMergeLinksAsync(ct));
            var settingKeys = LeaderboardCatalog.Boards.Select(b => b.SettingKey).OfType<string>().Distinct().ToList();
            var visibility = await _repo.GetVisibilityAsync(settingKeys, ct);
            var excluded = await _repo.GetExcludedUserIdsAsync(ct);
            var discoveries = await _repo.GetDiscoveriesAsync(ct);
            var sumKeys = LeaderboardCatalog.SourceMetricKeys.Where(k => Aggregation(k) == StatisticAggregation.Sum).ToList();
            var maxKeys = LeaderboardCatalog.SourceMetricKeys.Where(k => Aggregation(k) == StatisticAggregation.Max).ToList();
            var maxEntries = Math.Max(1, _options.MaxEntriesPerBoard);
            var noRows = new List<PlayerStatVisibility>();

            var written = 0;
            foreach (var period in LeaderboardCatalog.Periods)
            {
                (DateOnly Start, DateOnly EndExclusive)? range = period switch
                {
                    LeaderboardPeriod.Weekly => StatisticsPeriods.Resolve(today, StatisticsPeriodKind.Week),
                    LeaderboardPeriod.Monthly => StatisticsPeriods.Resolve(today, StatisticsPeriodKind.Month),
                    _ => null
                };
                var rows = range == null
                    ? await _repo.GetTotalsAsync(LeaderboardCatalog.SourceMetricKeys, ct)
                    : await _repo.GetDailyAggregatesAsync(sumKeys, maxKeys, range.Value.Start, range.Value.EndExclusive, ct);
                DateTime? fromUtc = range == null ? null : StatisticsPeriods.StartOfDayUtc(range.Value.Start, zone);
                DateTime? toUtc = range == null ? null : StatisticsPeriods.StartOfDayUtc(range.Value.EndExclusive, zone);

                var values = LeaderboardCatalog.Boards.ToDictionary(b => b.BoardKey, b => b.IsDiscoveries
                    ? LeaderboardEligibility.DiscoveryValues(discoveries, primaryOf, fromUtc, toUtc)
                    : LeaderboardEligibility.Values(b, rows, primaryOf));
                var users = await _repo.GetUsersAsync(values.Values.SelectMany(v => v.Select(c => c.UserId)).Distinct().ToList(), ct);

                foreach (var board in LeaderboardCatalog.Boards)
                {
                    var eligible = values[board.BoardKey].Where(c =>
                        users.TryGetValue(c.UserId, out var user) && user.IsActive
                        && !excluded.Contains(c.UserId)
                        && LeaderboardEligibility.IsEligible(board, visibility.GetValueOrDefault(c.UserId) ?? noRows));
                    var ranked = LeaderboardEligibility.Rank(eligible);
                    await _repo.ReplaceCurrentAsync(new LeaderboardSnapshot
                    {
                        BoardKey = board.BoardKey,
                        Period = period,
                        PeriodStart = range?.Start,
                        GeneratedAt = now,
                        EntryCount = ranked.Count,
                        Entries = ranked.Take(maxEntries).Select(r => new LeaderboardSnapshotEntry
                        {
                            Rank = r.Rank,
                            UserId = r.UserId,
                            Value = r.Value,
                            ReachedAt = r.ReachedAt
                        }).ToList()
                    }, ct);
                    written++;
                }
            }

            var purged = await _repo.PurgeSnapshotsBeforeAsync(now.AddDays(-Math.Max(1, _options.SnapshotRetentionDays)), ct);
            if (purged > 0)
            {
                _logger.LogInformation("Leaderboards: purged {Count} closed-period snapshots", purged);
            }
            return written;
        }

        private static StatisticAggregation Aggregation(string metricKey) =>
            StatisticsCatalog.FindMetric(metricKey)?.Aggregation ?? StatisticAggregation.Sum;
    }
}
