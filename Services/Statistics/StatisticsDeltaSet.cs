using System;
using System.Collections.Generic;
using System.Linq;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Services.Statistics
{
    public readonly record struct DailyKey(int UserId, DateOnly Day, string MetricKey, string ContextKey);

    public readonly record struct TotalKey(int UserId, string MetricKey, string ContextKey);

    public readonly record struct KillPairKey(int KillerUserId, int VictimUserId, DateOnly Day, string ContextKey);

    /// <summary>A pre-aggregated change of one row: added (sum) or offered as a new maximum (max).
    /// <see cref="At"/> is when the value was reached (latest contribution for sums, the
    /// maximum's own time for records).</summary>
    public readonly record struct StatisticDelta(StatisticAggregation Aggregation, decimal Value, DateTime At);

    /// <summary>
    /// The statistic changes of one ingestion batch or projection run, pre-aggregated in memory per
    /// row key (IMPLEMENTATION_PLAN.md §3.1) so the repository writes each row once with a
    /// multi-row upsert. Every daily change also changes the lifetime total of the same
    /// (user, metric, context).
    /// </summary>
    public sealed class StatisticsDeltaSet
    {
        private readonly Dictionary<DailyKey, StatisticDelta> _daily = new();
        private readonly Dictionary<TotalKey, StatisticDelta> _totals = new();
        private readonly Dictionary<KillPairKey, int> _killPairs = new();

        public IReadOnlyDictionary<DailyKey, StatisticDelta> Daily => _daily;

        public IReadOnlyDictionary<TotalKey, StatisticDelta> Totals => _totals;

        public IReadOnlyDictionary<KillPairKey, int> KillPairs => _killPairs;

        public bool IsEmpty => _daily.Count == 0 && _killPairs.Count == 0;

        /// <summary>Adds <paramref name="value"/> on <paramref name="day"/> (and to the lifetime total).</summary>
        public void Add(int userId, DateOnly day, string metricKey, string contextKey, StatisticAggregation aggregation,
            decimal value, DateTime at)
        {
            Merge(_daily, new DailyKey(userId, day, metricKey, contextKey), aggregation, value, at);
            Merge(_totals, new TotalKey(userId, metricKey, contextKey), aggregation, value, at);
        }

        /// <summary>Adds one kill of <paramref name="victimUserId"/> by <paramref name="killerUserId"/>.</summary>
        public void AddKillPair(int killerUserId, int victimUserId, DateOnly day, string contextKey, int count = 1)
        {
            var key = new KillPairKey(killerUserId, victimUserId, day, contextKey);
            _killPairs[key] = _killPairs.TryGetValue(key, out var existing) ? existing + count : count;
        }

        /// <summary>Applies an aggregation to an existing stored value (the InMemory path and tests).</summary>
        public static (decimal Value, DateTime At, bool Changed) Combine(decimal current, DateTime currentAt, StatisticDelta delta)
        {
            if (delta.Aggregation == StatisticAggregation.Sum)
            {
                return (current + delta.Value, delta.At > currentAt ? delta.At : currentAt, true);
            }
            return delta.Value > current ? (delta.Value, delta.At, true) : (current, currentAt, false);
        }

        private static void Merge<TKey>(Dictionary<TKey, StatisticDelta> map, TKey key, StatisticAggregation aggregation,
            decimal value, DateTime at) where TKey : notnull
        {
            if (!map.TryGetValue(key, out var existing))
            {
                map[key] = new StatisticDelta(aggregation, value, at);
                return;
            }
            if (aggregation == StatisticAggregation.Sum)
            {
                map[key] = new StatisticDelta(aggregation, existing.Value + value, at > existing.At ? at : existing.At);
            }
            else if (value > existing.Value || (value == existing.Value && at < existing.At))
            {
                map[key] = new StatisticDelta(aggregation, value, at);
            }
        }

        /// <summary>Distinct user ids touched (for logging/metrics).</summary>
        public int UserCount => _daily.Keys.Select(k => k.UserId).Concat(_killPairs.Keys.Select(k => k.KillerUserId)).Distinct().Count();
    }
}
