namespace knkwebapi_v2.Enums;

/// <summary>How a statistic combines values (DESIGN.md §F.1): counters, durations and distances
/// add up; records (highest killstreak, highest fall) keep the highest value.</summary>
public enum StatisticAggregation : byte
{
    Sum = 0,
    Max = 1
}
