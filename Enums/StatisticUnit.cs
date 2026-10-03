namespace knkwebapi_v2.Enums;

/// <summary>Unit of a statistic's value; decides display rounding (StatisticsFormatting,
/// DESIGN.md §F.10).</summary>
public enum StatisticUnit : byte
{
    Count = 0,
    Seconds = 1,
    Blocks = 2,
    Points = 3
}
