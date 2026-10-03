using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Statistics
{
    /// <summary>
    /// Projects the currency ledger into statistics (KNG-34, DESIGN.md §F.5, D9, D13;
    /// IMPLEMENTATION_PLAN.md §4): user legs of currency_entries with Id &gt; the "ledger" cursor,
    /// ascending, 2,000 per run →
    /// <list type="bullet">
    /// <item><c>coins_earned/spent</c>, <c>gems_earned/spent</c>, <c>xp_gained</c> per
    /// <see cref="LedgerStatisticsClassifier"/> (a reversal negates the reversed transaction's
    /// bucket, on the reversal's day; the sign of the leg is authoritative);</item>
    /// <item><c>player_title_changes</c> for every XP leg whose balance before and after resolve to
    /// different title brackets (same rule as TitleService.ResolveAsync), with the user's gendered
    /// names at projection time.</item>
    /// </list>
    /// The cursor advances in the same transaction as the projected rows, so a run is projected
    /// exactly once. Legs younger than Statistics:ProjectionSafetyLagSeconds wait for the next run
    /// (a lower id may still be committing). Reads the ledger, never writes it.
    /// </summary>
    public class LedgerStatisticsProjector
    {
        public const string CursorName = "ledger";
        public const string ProjectorName = "ledger";
        public const int ChunkSize = 2000;

        /// <summary>Metric keys this projector owns (deleted and recomputed by a rebuild).</summary>
        public static IReadOnlyList<string> ProjectedMetricKeys { get; } = new[]
        {
            StatisticsCatalog.CoinsEarned, StatisticsCatalog.CoinsSpent, StatisticsCatalog.GemsEarned,
            StatisticsCatalog.GemsSpent, StatisticsCatalog.XpGained
        };

        private readonly IStatisticsRepository _repo;
        private readonly StatisticsOptions _options;
        private readonly StatisticsMetrics? _metrics;
        private readonly ILogger<LedgerStatisticsProjector> _logger;
        private readonly TimeProvider _time;

        public LedgerStatisticsProjector(IStatisticsRepository repo, IOptions<StatisticsOptions>? options = null,
            StatisticsMetrics? metrics = null, ILogger<LedgerStatisticsProjector>? logger = null, TimeProvider? time = null)
        {
            _repo = repo;
            _options = options?.Value ?? new StatisticsOptions();
            _metrics = metrics;
            _logger = logger ?? NullLogger<LedgerStatisticsProjector>.Instance;
            _time = time ?? TimeProvider.System;
        }

        /// <summary>Projects the next chunk; returns the number of legs projected (0 = caught up).</summary>
        public Task<int> ProjectNextAsync(CancellationToken ct = default) =>
            _repo.InTransactionAsync(async () =>
            {
                var now = _time.GetUtcNow().UtcDateTime;
                var cursor = await _repo.GetOrCreateCursorAsync(CursorName, ct);
                var legs = await _repo.GetLedgerLegsAsync(cursor.LastSourceId, ChunkSize, ct: ct);
                var cutoff = now.AddSeconds(-Math.Max(0, _options.ProjectionSafetyLagSeconds));
                // Stop at the first leg that is too young: the cursor must never pass it.
                var ready = legs.TakeWhile(l => Utc(l.CreatedAt) <= cutoff).ToList();
                if (ready.Count == 0)
                {
                    await _repo.SaveChangesAsync(ct); // a new cursor row
                    return 0;
                }

                var deltas = new StatisticsDeltaSet();
                var changes = await BuildAsync(ready, deltas, ct);
                _repo.AddTitleChanges(changes);
                cursor.LastSourceId = ready[^1].EntryId;
                cursor.UpdatedAt = now;
                await _repo.ApplyAsync(deltas, now, ct);

                _metrics?.RecordProjectionLag(ProjectorName, now - Utc(ready[^1].CreatedAt));
                return ready.Count;
            }, ct);

        /// <summary>
        /// Rebuild (IMPLEMENTATION_PLAN.md §4 StatisticsRebuildService). All users: deletes every
        /// ledger-projected row and title change and resets the cursor — the next runs re-project
        /// from the start. One user: deletes theirs and re-projects their legs up to the cursor in
        /// one transaction.
        /// </summary>
        public Task<int> RebuildAsync(int? userId, CancellationToken ct = default) =>
            _repo.InTransactionAsync(async () =>
            {
                var now = _time.GetUtcNow().UtcDateTime;
                var cursor = await _repo.GetOrCreateCursorAsync(CursorName, ct);
                await _repo.DeleteMetricRowsAsync(ProjectedMetricKeys, null, userId, ct);
                await _repo.DeleteTitleChangesAsync(userId, ct);
                if (userId == null)
                {
                    cursor.LastSourceId = 0;
                    cursor.UpdatedAt = now;
                    await _repo.SaveChangesAsync(ct);
                    return 0;
                }

                var projected = 0;
                long after = 0;
                while (true)
                {
                    var legs = await _repo.GetLedgerLegsAsync(after, ChunkSize, userId, cursor.LastSourceId, ct);
                    if (legs.Count == 0) break;
                    var deltas = new StatisticsDeltaSet();
                    _repo.AddTitleChanges(await BuildAsync(legs, deltas, ct));
                    await _repo.ApplyAsync(deltas, now, ct);
                    projected += legs.Count;
                    after = legs[^1].EntryId;
                }
                await _repo.SaveChangesAsync(ct);
                return projected;
            }, ct);

        private async Task<List<PlayerTitleChange>> BuildAsync(List<LedgerLegRow> legs, StatisticsDeltaSet deltas, CancellationToken ct)
        {
            var zone = StatisticsPeriods.FindZone(_options.TimeZone);
            var reversedReasons = await ResolveReversedReasonsAsync(legs, ct);

            foreach (var leg in legs)
            {
                var bucket = LedgerStatisticsClassifier.Classify(leg.ReasonCode);
                if (bucket == LedgerBucket.Reversal)
                {
                    bucket = leg.ReversesTransactionId != null && reversedReasons.TryGetValue(leg.ReversesTransactionId.Value, out var origin)
                        ? LedgerStatisticsClassifier.Classify(origin)
                        : LedgerBucket.Excluded;
                }
                var (metric, value) = MetricFor(bucket, leg.Currency, leg.Amount);
                if (metric == null || value == 0) continue;
                var at = Utc(leg.CreatedAt);
                deltas.Add(leg.UserId, StatisticsPeriods.LocalDay(at, zone), metric, "", StatisticAggregation.Sum, value, at);
            }

            return await BuildTitleChangesAsync(legs, ct);
        }

        /// <summary>The statistic a classified leg changes and by how much (earned: + amount; spent:
        /// − amount, so a spend's negative leg counts up and its reversal counts down).</summary>
        public static (string? Metric, decimal Value) MetricFor(LedgerBucket bucket, Currency currency, long amount) =>
            (bucket, currency) switch
            {
                (LedgerBucket.Earned, Currency.Coins) => (StatisticsCatalog.CoinsEarned, amount),
                (LedgerBucket.Earned, Currency.Gems) => (StatisticsCatalog.GemsEarned, amount),
                (LedgerBucket.Earned, Currency.Experience) => (StatisticsCatalog.XpGained, amount),
                (LedgerBucket.Spent, Currency.Coins) => (StatisticsCatalog.CoinsSpent, -amount),
                (LedgerBucket.Spent, Currency.Gems) => (StatisticsCatalog.GemsSpent, -amount),
                _ => (null, 0m)
            };

        /// <summary>For every reversal leg's reversed transaction, the first non-REVERSAL reason up the
        /// chain (a reversal of a reversal takes the original's bucket again).</summary>
        private async Task<Dictionary<long, string>> ResolveReversedReasonsAsync(List<LedgerLegRow> legs, CancellationToken ct)
        {
            var resolved = new Dictionary<long, string>();
            var pending = legs.Where(l => l.ReasonCode == CurrencyReasons.Reversal && l.ReversesTransactionId != null)
                .Select(l => l.ReversesTransactionId!.Value).Distinct().ToList();
            if (pending.Count == 0) return resolved;

            // Each start id walks up its chain (bounded); intermediate lookups are batched per level.
            var current = pending.ToDictionary(id => id, id => id);
            for (var depth = 0; depth < 8 && current.Count > 0; depth++)
            {
                var reasons = await _repo.GetTransactionReasonsAsync(current.Values.Distinct().ToList(), ct);
                var next = new Dictionary<long, long>();
                foreach (var (start, at) in current)
                {
                    if (!reasons.TryGetValue(at, out var info)) continue; // unknown: excluded
                    if (info.ReasonCode == CurrencyReasons.Reversal && info.ReversesTransactionId != null)
                    {
                        next[start] = info.ReversesTransactionId.Value;
                    }
                    else
                    {
                        resolved[start] = info.ReasonCode;
                    }
                }
                current = next;
            }
            return resolved;
        }

        private async Task<List<PlayerTitleChange>> BuildTitleChangesAsync(List<LedgerLegRow> legs, CancellationToken ct)
        {
            var xpLegs = legs.Where(l => l.Currency == Currency.Experience && l.BalanceBefore != l.BalanceAfter).ToList();
            if (xpLegs.Count == 0) return new List<PlayerTitleChange>();

            var brackets = await _repo.GetTitleBracketsAsync(ct);
            if (brackets.Count == 0) return new List<PlayerTitleChange>();
            var existing = await _repo.GetExistingTitleChangeEntryIdsAsync(xpLegs.Select(l => l.EntryId).ToList(), ct);
            var genders = (await _repo.GetUsersAsync(xpLegs.Select(l => l.UserId).Distinct().ToList(), ct))
                .ToDictionary(u => u.Id, u => u.Gender);

            var changes = new List<PlayerTitleChange>();
            foreach (var leg in xpLegs)
            {
                if (existing.Contains(leg.EntryId)) continue;
                var change = TitleChangeFor(brackets, leg, genders.TryGetValue(leg.UserId, out var gender) ? gender : null);
                if (change != null) changes.Add(change);
            }
            if (changes.Count > 0)
            {
                _logger.LogDebug("Projected {Count} title changes", changes.Count);
            }
            return changes;
        }

        /// <summary>The title change an XP leg causes, or null when both balances resolve to the same
        /// bracket. <paramref name="brackets"/> ascending by MinExperience.</summary>
        public static PlayerTitleChange? TitleChangeFor(IReadOnlyList<TitleBracket> brackets, LedgerLegRow leg, Gender? gender)
        {
            var before = BracketFor(brackets, leg.BalanceBefore);
            var after = BracketFor(brackets, leg.BalanceAfter);
            if (before == null || after == null || before.Id == after.Id) return null;
            return new PlayerTitleChange
            {
                UserId = leg.UserId,
                FromTitleBracketId = before.Id,
                FromTitleName = Truncate(before.NameFor(gender)),
                ToTitleBracketId = after.Id,
                ToTitleName = Truncate(after.NameFor(gender)),
                Direction = after.MinExperience > before.MinExperience ? TitleChangeDirection.Promotion : TitleChangeDirection.Demotion,
                ExperienceBefore = leg.BalanceBefore,
                ExperienceAfter = leg.BalanceAfter,
                CurrencyEntryId = leg.EntryId,
                ChangedAt = Utc(leg.CreatedAt)
            };
        }

        /// <summary>TitleService's rule: the highest bracket whose MinExperience ≤ xp, else the lowest.</summary>
        public static TitleBracket? BracketFor(IReadOnlyList<TitleBracket> brackets, long experience)
        {
            if (brackets.Count == 0) return null;
            return brackets.LastOrDefault(b => b.MinExperience <= experience) ?? brackets[0];
        }

        private static string Truncate(string name) => name.Length <= 64 ? name : name[..64];

        private static DateTime Utc(DateTime value) =>
            value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}
