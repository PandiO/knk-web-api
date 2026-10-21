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
    /// Projects arena duel results into wins / losses / draws in context "duel" (arena duels KNG-221,
    /// DESIGN.md §10.3, D10, D17), onto the local day the match ended:
    /// <list type="bullet">
    /// <item>Completed with a winner (Win or Forfeit) → the winner a win, the other duelist a loss (a
    /// forfeit is a loss for the quitter);</item>
    /// <item>Completed without a winner (TimeLimit, AgreedDraw, SimultaneousDeath) → a draw for both;</item>
    /// <item>Cancelled and Aborted count nothing; a Held result counts once staff release it, a Voided
    /// one never.</item>
    /// </list>
    /// Unlike a Siege match, a duel result can still change after it was counted: staff can revert or
    /// confiscate a paid result (Review = Voided). Such a match is then retracted once (−1 on the day it
    /// was counted), marked by a second projected-source row, so the counted set is always "projected
    /// and not retracted". Reads duel_matches, never writes it.
    /// </summary>
    public class DuelStatisticsProjector
    {
        public const string ProjectorName = "duel";
        public const int BatchSize = 50;

        /// <summary>Metric keys this projector owns in context "duel".</summary>
        public static IReadOnlyList<string> ProjectedMetricKeys { get; } = new[]
        {
            StatisticsCatalog.Wins, StatisticsCatalog.Losses, StatisticsCatalog.Draws
        };

        private readonly IStatisticsRepository _repo;
        private readonly StatisticsOptions _options;
        private readonly StatisticsMetrics? _metrics;
        private readonly ILogger<DuelStatisticsProjector> _logger;
        private readonly TimeProvider _time;

        public DuelStatisticsProjector(IStatisticsRepository repo, IOptions<StatisticsOptions>? options = null,
            StatisticsMetrics? metrics = null, ILogger<DuelStatisticsProjector>? logger = null, TimeProvider? time = null)
        {
            _repo = repo;
            _options = options?.Value ?? new StatisticsOptions();
            _metrics = metrics;
            _logger = logger ?? NullLogger<DuelStatisticsProjector>.Instance;
            _time = time ?? TimeProvider.System;
        }

        /// <summary>Counts the next decided matches and retracts the next voided ones; returns how many
        /// (0 = caught up).</summary>
        public Task<int> ProjectNextAsync(CancellationToken ct = default) =>
            _repo.InTransactionAsync(async () =>
            {
                var toCount = await _repo.GetUnprojectedDuelMatchesAsync(BatchSize, ct);
                var toRetract = await _repo.GetDuelMatchesToRetractAsync(BatchSize, ct);
                if (toCount.Count == 0 && toRetract.Count == 0) return 0;

                var now = _time.GetUtcNow().UtcDateTime;
                var zone = StatisticsPeriods.FindZone(_options.TimeZone);
                var deltas = new StatisticsDeltaSet();
                foreach (var match in toCount)
                {
                    Project(match, deltas, zone, null, 1, _logger);
                }
                foreach (var match in toRetract)
                {
                    Project(match, deltas, zone, null, -1, _logger);
                }
                _repo.AddProjectedSources(toCount.Select(m => Source(StatisticsRepository.DuelMatchSourceType, m, now))
                    .Concat(toRetract.Select(m => Source(StatisticsRepository.DuelMatchRetractedSourceType, m, now))));
                await _repo.ApplyAsync(deltas, now, ct);

                if (toCount.Count > 0)
                {
                    var newest = toCount.Max(m => m.EndedAt!.Value);
                    _metrics?.RecordProjectionLag(ProjectorName, now - DateTime.SpecifyKind(newest, DateTimeKind.Utc));
                }
                return toCount.Count + toRetract.Count;
            }, ct);

        /// <summary>All users: deletes the duel rows and both kinds of projected-source markers (the next
        /// runs re-project every decided, non-voided match). One user: deletes theirs and re-projects the
        /// matches that are counted now (projected and not retracted).</summary>
        public Task<int> RebuildAsync(int? userId, CancellationToken ct = default) =>
            _repo.InTransactionAsync(async () =>
            {
                await _repo.DeleteMetricRowsAsync(ProjectedMetricKeys, StatisticsCatalog.Duel, userId, ct);
                if (userId == null)
                {
                    await _repo.DeleteProjectedSourcesAsync(StatisticsRepository.DuelMatchSourceType, ct);
                    await _repo.DeleteProjectedSourcesAsync(StatisticsRepository.DuelMatchRetractedSourceType, ct);
                    return 0;
                }

                var now = _time.GetUtcNow().UtcDateTime;
                var zone = StatisticsPeriods.FindZone(_options.TimeZone);
                var matches = await _repo.GetCountedDuelMatchesOfUserAsync(userId.Value, ct);
                var deltas = new StatisticsDeltaSet();
                foreach (var match in matches)
                {
                    Project(match, deltas, zone, userId, 1, _logger);
                }
                await _repo.ApplyAsync(deltas, now, ct);
                return matches.Count;
            }, ct);

        /// <summary>Whether a match's result counts now: completed, ended, and not held or voided.</summary>
        public static bool Counts(DuelMatch match) =>
            match.Status == DuelMatchStatus.Completed && match.EndedAt != null
            && match.Review != DuelReviewState.Held && match.Review != DuelReviewState.Voided;

        /// <summary>Adds one match's result (optionally of one user only) to <paramref name="deltas"/>, with
        /// <paramref name="sign"/> 1 (count) or −1 (retract). Doesn't check <see cref="Counts"/>.</summary>
        public static void Project(DuelMatch match, StatisticsDeltaSet deltas, TimeZoneInfo zone, int? onlyUserId, int sign,
            ILogger? logger = null)
        {
            if (match.EndedAt == null || match.Status != DuelMatchStatus.Completed) return;
            var endedAt = DateTime.SpecifyKind(match.EndedAt.Value, DateTimeKind.Utc);
            var day = StatisticsPeriods.LocalDay(endedAt, zone);

            foreach (var userId in new[] { match.ChallengerUserId, match.OpponentUserId })
            {
                if (onlyUserId != null && userId != onlyUserId) continue;
                var result = ResultFor(match, userId);
                if (result == null)
                {
                    logger?.LogWarning("Duel match {MatchId}: {Reason} without a winner; no result counted", match.Id, match.EndReason);
                    return;
                }
                deltas.Add(userId, day, result, StatisticsCatalog.Duel, StatisticAggregation.Sum, sign, endedAt);
            }
        }

        /// <summary>wins / losses / draws of <paramref name="userId"/> in a completed match, or null when a
        /// decisive end reason has no winner (a broken row).</summary>
        public static string? ResultFor(DuelMatch match, int userId)
        {
            if (match.EndReason is DuelEndReason.Win or DuelEndReason.Forfeit)
            {
                if (match.WinnerUserId == null) return null;
                return match.WinnerUserId == userId ? StatisticsCatalog.Wins : StatisticsCatalog.Losses;
            }
            return match.WinnerUserId == null ? StatisticsCatalog.Draws
                : match.WinnerUserId == userId ? StatisticsCatalog.Wins : StatisticsCatalog.Losses;
        }

        private static StatisticsProjectedSource Source(string type, DuelMatch match, DateTime now) =>
            new() { SourceType = type, SourceId = match.Id, ProjectedAt = now };
    }
}
