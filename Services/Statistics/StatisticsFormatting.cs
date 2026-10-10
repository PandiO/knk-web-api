using System;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Services.Statistics
{
    /// <summary>
    /// The presentation rounding contract (DESIGN.md §F.10): applied to a final total only, never
    /// to single hits or ticks. Points (damage, gate damage) → whole points, half away from zero;
    /// blocks (distance) → whole blocks, floored; highest fall → one decimal; seconds → whole
    /// seconds, floored; counts → whole numbers.
    /// </summary>
    public static class StatisticsFormatting
    {
        public static decimal Display(StatisticMetricDefinition metric, decimal raw)
        {
            if (metric.Key == StatisticsCatalog.HighestFall)
            {
                return Math.Round(raw, 1, MidpointRounding.AwayFromZero);
            }
            return metric.Unit switch
            {
                StatisticUnit.Points => Math.Round(raw, 0, MidpointRounding.AwayFromZero),
                StatisticUnit.Blocks => Math.Floor(raw),
                StatisticUnit.Seconds => Math.Floor(raw),
                _ => Math.Round(raw, 0, MidpointRounding.AwayFromZero)
            };
        }

        public static decimal Display(string metricKey, decimal raw)
        {
            var metric = StatisticsCatalog.FindMetric(metricKey)
                ?? throw new ArgumentException($"Unknown statistics metric '{metricKey}'.", nameof(metricKey));
            return Display(metric, raw);
        }
    }
}
