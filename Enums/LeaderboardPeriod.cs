namespace knkwebapi_v2.Enums;

/// <summary>Leaderboard period (DESIGN.md "Leaderboards", §F.11): the current Monday week, the
/// current calendar month, or all time — boundaries from StatisticsPeriods (D5).</summary>
public enum LeaderboardPeriod : byte
{
    Weekly = 0,
    Monthly = 1,
    Lifetime = 2
}
