using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace knkwebapi_v2.Services.Statistics
{
    /// <summary>A calendar period of the statistics views (DESIGN.md D5, §F.10).</summary>
    public enum StatisticsPeriodKind
    {
        Day,
        Week,
        Month
    }

    /// <summary>
    /// The one place that computes statistics period boundaries (KNG-34 D5, DESIGN.md §F.10): local
    /// day, week starting Monday, calendar month, all in a given time zone (Statistics:TimeZone,
    /// default Europe/Amsterdam). Every method takes the zone so a per-player zone can be passed
    /// later without touching callers' semantics. Pure.
    /// </summary>
    public static class StatisticsPeriods
    {
        private static readonly ConcurrentDictionary<string, TimeZoneInfo> Zones = new(StringComparer.Ordinal);

        /// <summary>
        /// The zone for an IANA (or Windows) id; UTC when the id is empty or unknown on this host, so
        /// a bad setting degrades to UTC days instead of failing every request.
        /// </summary>
        public static TimeZoneInfo FindZone(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
            return Zones.GetOrAdd(id, key =>
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(key);
                }
                catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                    return TimeZoneInfo.Utc;
                }
            });
        }

        /// <summary>The local calendar day of a UTC instant.</summary>
        public static DateOnly LocalDay(DateTime utc, TimeZoneInfo zone) =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), zone));

        /// <summary>The period containing a UTC instant, as local days [start, endExclusive).</summary>
        public static (DateOnly Start, DateOnly EndExclusive) Resolve(DateTime utc, StatisticsPeriodKind kind, TimeZoneInfo zone) =>
            Resolve(LocalDay(utc, zone), kind);

        /// <summary>The period containing a local day, as local days [start, endExclusive).</summary>
        public static (DateOnly Start, DateOnly EndExclusive) Resolve(DateOnly day, StatisticsPeriodKind kind)
        {
            switch (kind)
            {
                case StatisticsPeriodKind.Day:
                    return (day, day.AddDays(1));
                case StatisticsPeriodKind.Week:
                    // DayOfWeek: Sunday = 0 … Saturday = 6; weeks start on Monday (D5).
                    var offset = ((int)day.DayOfWeek + 6) % 7;
                    var monday = day.AddDays(-offset);
                    return (monday, monday.AddDays(7));
                case StatisticsPeriodKind.Month:
                    var first = new DateOnly(day.Year, day.Month, 1);
                    return (first, first.AddMonths(1));
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        /// <summary>The UTC instant at which a local day starts (local midnight, or the first valid
        /// local time after it when a DST jump skips midnight).</summary>
        public static DateTime StartOfDayUtc(DateOnly day, TimeZoneInfo zone)
        {
            var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var guard = 0;
            while (zone.IsInvalidTime(local) && guard++ < 24 * 60)
            {
                local = local.AddMinutes(1);
            }
            return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }

        /// <summary>
        /// Splits the UTC interval [fromUtc, toUtc) at local midnights: one (day, seconds) per local
        /// day it touches, in order, seconds summing to the interval's length (DESIGN.md §F.10 —
        /// a session spanning midnight is allocated to both days). Empty when toUtc ≤ fromUtc.
        /// </summary>
        public static IEnumerable<(DateOnly Day, double Seconds)> SplitByDay(DateTime fromUtc, DateTime toUtc, TimeZoneInfo zone)
        {
            var cursor = AsUtc(fromUtc);
            var end = AsUtc(toUtc);
            while (cursor < end)
            {
                var day = LocalDay(cursor, zone);
                var nextStart = StartOfDayUtc(day.AddDays(1), zone);
                var sliceEnd = nextStart < end ? nextStart : end;
                if (sliceEnd <= cursor)
                {
                    // Defensive: never loop forever on a pathological zone rule.
                    sliceEnd = end;
                }
                yield return (day, (sliceEnd - cursor).TotalSeconds);
                cursor = sliceEnd;
            }
        }

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
