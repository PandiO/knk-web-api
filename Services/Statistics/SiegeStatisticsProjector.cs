using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Statistics
{
    /// <summary>
    /// Projects Siege match history into statistics (KNG-34, DESIGN.md §F.6, D1, L1-7/L1-8; moved
    /// to link 2 by link 1, L1-22). Every Completed or Aborted match with an end time is projected
    /// once (a statistics_projected_sources row in the same transaction), onto the local day the
    /// match ended, in context "siege":
    /// <list type="bullet">
    /// <item>in-match Kills → pvp_kills, Deaths → deaths, HighestKillStreak → highest_killstreak
    /// (max), Captures → objectives_captured — for aborted matches too (they happened);</item>
    /// <item>Completed only: left early (LeftAt &lt; EndedAt) → loss whatever the outcome; present
    /// at the end (LeftAt null or ≥ EndedAt) → win when the team's AllianceGroup is the winner,
    /// loss otherwise, draw when there is no winner; no team row → no result (logged).</item>
    /// </list>
    /// Several participant rows of one user in a match count once: stats summed (streak: max), and
    /// any early leave makes it a loss (a left-and-rejoined player keeps the left marker, L1-7).
    /// Reads the Siege tables, never writes them.
    /// </summary>
    public class SiegeStatisticsProjector
    {
        public const string ProjectorName = "siege";
        public const int BatchSize = 50;

        /// <summary>Metric keys this projector owns in context "siege".</summary>
        public static IReadOnlyList<string> ProjectedMetricKeys { get; } = new[]
        {
            StatisticsCatalog.PvpKills, StatisticsCatalog.Deaths, StatisticsCatalog.HighestKillstreak,
            StatisticsCatalog.ObjectivesCaptured, StatisticsCatalog.Wins, StatisticsCatalog.Losses, StatisticsCatalog.Draws
        };

        private readonly IStatisticsRepository _repo;
        private readonly StatisticsOptions _options;
        private readonly StatisticsMetrics? _metrics;
        private readonly ILogger<SiegeStatisticsProjector> _logger;
        private readonly TimeProvider _time;

        public SiegeStatisticsProjector(IStatisticsRepository repo, IOptions<StatisticsOptions>? options = null,
            StatisticsMetrics? metrics = null, ILogger<SiegeStatisticsProjector>? logger = null, TimeProvider? time = null)
        {
            _repo = repo;
            _options = options?.Value ?? new StatisticsOptions();
            _metrics = metrics;
            _logger = logger ?? NullLogger<SiegeStatisticsProjector>.Instance;
            _time = time ?? TimeProvider.System;
        }

        /// <summary>Projects the next unprojected matches; returns how many (0 = caught up).</summary>
        public Task<int> ProjectNextAsync(CancellationToken ct = default) =>
            _repo.InTransactionAsync(async () =>
            {
                var matches = await _repo.GetUnprojectedSiegeMatchesAsync(BatchSize, ct);
                if (matches.Count == 0) return 0;

                var now = _time.GetUtcNow().UtcDateTime;
                var zone = StatisticsPeriods.FindZone(_options.TimeZone);
                var deltas = new StatisticsDeltaSet();
                foreach (var match in matches)
                {
                    Project(match, deltas, zone, null, _logger);
                }
                _repo.AddProjectedSources(matches.Select(m => new StatisticsProjectedSource
                {
                    SourceType = StatisticsRepository.SiegeMatchSourceType,
                    SourceId = m.Id,
                    ProjectedAt = now
                }));
                await _repo.ApplyAsync(deltas, now, ct);

                var newest = matches.Max(m => m.EndedAt!.Value);
                _metrics?.RecordProjectionLag(ProjectorName, now - DateTime.SpecifyKind(newest, DateTimeKind.Utc));
                return matches.Count;
            }, ct);

        /// <summary>All users: deletes the Siege-owned rows and the projected-source markers (the next
        /// runs re-project every match). One user: deletes theirs and re-projects their matches.</summary>
        public Task<int> RebuildAsync(int? userId, CancellationToken ct = default) =>
            _repo.InTransactionAsync(async () =>
            {
                await _repo.DeleteMetricRowsAsync(ProjectedMetricKeys, StatisticsCatalog.Siege, userId, ct);
                if (userId == null)
                {
                    await _repo.DeleteProjectedSourcesAsync(StatisticsRepository.SiegeMatchSourceType, ct);
                    return 0;
                }

                var now = _time.GetUtcNow().UtcDateTime;
                var zone = StatisticsPeriods.FindZone(_options.TimeZone);
                var matches = await _repo.GetProjectedSiegeMatchesOfUserAsync(userId.Value, ct);
                var deltas = new StatisticsDeltaSet();
                foreach (var match in matches)
                {
                    Project(match, deltas, zone, userId, _logger);
                }
                await _repo.ApplyAsync(deltas, now, ct);
                return matches.Count;
            }, ct);

        /// <summary>Adds one match's statistics (optionally of one user only) to <paramref name="deltas"/>.</summary>
        public static void Project(SiegeMatch match, StatisticsDeltaSet deltas, TimeZoneInfo zone, int? onlyUserId, ILogger? logger = null)
        {
            if (match.EndedAt == null) return;
            var endedAt = DateTime.SpecifyKind(match.EndedAt.Value, DateTimeKind.Utc);
            var day = StatisticsPeriods.LocalDay(endedAt, zone);
            var completed = match.Status == SiegeMatchStatus.Completed;

            foreach (var rows in match.Participants.GroupBy(p => p.UserId))
            {
                var userId = rows.Key;
                if (onlyUserId != null && userId != onlyUserId) continue;

                void Add(string metric, StatisticAggregation aggregation, decimal value)
                {
                    if (value > 0) deltas.Add(userId, day, metric, StatisticsCatalog.Siege, aggregation, value, endedAt);
                }

                Add(StatisticsCatalog.PvpKills, StatisticAggregation.Sum, rows.Sum(p => p.Kills));
                Add(StatisticsCatalog.Deaths, StatisticAggregation.Sum, rows.Sum(p => p.Deaths));
                Add(StatisticsCatalog.HighestKillstreak, StatisticAggregation.Max, rows.Max(p => p.HighestKillStreak));
                Add(StatisticsCatalog.ObjectivesCaptured, StatisticAggregation.Sum, rows.Sum(p => p.Captures));

                if (!completed) continue; // aborted: no win, loss or draw (D1)

                var result = ResultFor(match, rows.ToList(), endedAt);
                if (result == null)
                {
                    logger?.LogWarning("Siege match {MatchId}: participant user {UserId} has no team row; no result counted", match.Id, userId);
                    continue;
                }
                Add(result, StatisticAggregation.Sum, 1);
            }
        }

        /// <summary>wins / losses / draws for a completed match, or null when undecidable (no team).</summary>
        public static string? ResultFor(SiegeMatch match, IReadOnlyList<SiegeMatchParticipant> rows, DateTime endedAtUtc)
        {
            if (rows.Any(p => p.LeftAt != null && DateTime.SpecifyKind(p.LeftAt.Value, DateTimeKind.Utc) < endedAtUtc))
            {
                return StatisticsCatalog.Losses; // leaving early is a loss (D1)
            }
            var team = rows.Select(p => p.SiegeTeam).FirstOrDefault(t => t != null);
            if (team == null) return null;
            if (match.WinningAllianceGroup == null) return StatisticsCatalog.Draws;
            return team.AllianceGroup == match.WinningAllianceGroup ? StatisticsCatalog.Wins : StatisticsCatalog.Losses;
        }
    }
}
