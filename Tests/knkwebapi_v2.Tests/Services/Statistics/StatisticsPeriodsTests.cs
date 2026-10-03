using knkwebapi_v2.Services.Statistics;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Statistics;

/// <summary>KNG-34 D5 / DESIGN.md §F.10: Monday weeks, calendar months, local midnights in
/// Europe/Amsterdam including both DST change days.</summary>
public class StatisticsPeriodsTests
{
    private static readonly TimeZoneInfo Amsterdam = StatisticsPeriods.FindZone("Europe/Amsterdam");

    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("2026-09-28", "2026-09-28")] // Monday
    [InlineData("2026-10-03", "2026-09-28")] // Saturday
    [InlineData("2026-10-04", "2026-09-28")] // Sunday belongs to the week that started on Monday
    [InlineData("2026-10-05", "2026-10-05")]
    [InlineData("2027-01-01", "2026-12-28")] // across a year boundary
    public void Week_StartsOnMonday(string day, string monday)
    {
        var (start, end) = StatisticsPeriods.Resolve(DateOnly.Parse(day), StatisticsPeriodKind.Week);

        Assert.Equal(DateOnly.Parse(monday), start);
        Assert.Equal(start.AddDays(7), end);
        Assert.Equal(DayOfWeek.Monday, start.DayOfWeek);
    }

    [Theory]
    [InlineData("2026-10-31", "2026-10-01", "2026-11-01")]
    [InlineData("2028-02-29", "2028-02-01", "2028-03-01")] // leap year
    [InlineData("2026-12-15", "2026-12-01", "2027-01-01")]
    public void Month_IsTheCalendarMonth(string day, string first, string end)
    {
        Assert.Equal((DateOnly.Parse(first), DateOnly.Parse(end)), StatisticsPeriods.Resolve(DateOnly.Parse(day), StatisticsPeriodKind.Month));
    }

    [Fact]
    public void LocalDay_UsesTheZone_NotUtc()
    {
        // 22:30 UTC in summer (CEST, UTC+2) is 00:30 the next local day.
        Assert.Equal(new DateOnly(2026, 7, 2), StatisticsPeriods.LocalDay(Utc(2026, 7, 1, 22, 30), Amsterdam));
        // 23:30 UTC in winter (CET, UTC+1) is 00:30 the next local day; 22:59 is still the same day.
        Assert.Equal(new DateOnly(2026, 1, 2), StatisticsPeriods.LocalDay(Utc(2026, 1, 1, 23, 30), Amsterdam));
        Assert.Equal(new DateOnly(2026, 1, 1), StatisticsPeriods.LocalDay(Utc(2026, 1, 1, 22, 59), Amsterdam));
    }

    [Fact]
    public void ResolveFromInstant_UsesTheLocalDay()
    {
        // Sunday 2026-10-04 23:30 UTC is Monday 01:30 CEST: the next week.
        var (start, _) = StatisticsPeriods.Resolve(Utc(2026, 10, 4, 23, 30), StatisticsPeriodKind.Week, Amsterdam);
        Assert.Equal(new DateOnly(2026, 10, 5), start);
    }

    [Fact]
    public void SplitByDay_SplitsAtLocalMidnight()
    {
        // 21:30 → 23:15 UTC on 2026-07-01 = 23:30 → 01:15 CEST.
        var slices = StatisticsPeriods.SplitByDay(Utc(2026, 7, 1, 21, 30), Utc(2026, 7, 1, 23, 15), Amsterdam).ToList();

        Assert.Equal(new[] { (new DateOnly(2026, 7, 1), 1800d), (new DateOnly(2026, 7, 2), 4500d) }, slices);
    }

    [Fact]
    public void SplitByDay_SpringForwardDayHas23Hours()
    {
        // 2026-03-29: clocks jump 02:00 → 03:00 in Amsterdam. Local day = 2026-03-28 23:00 UTC → 03-29 22:00 UTC.
        var slices = StatisticsPeriods.SplitByDay(Utc(2026, 3, 28, 22, 0), Utc(2026, 3, 30, 0, 0), Amsterdam).ToList();

        Assert.Equal(3, slices.Count);
        Assert.Equal((new DateOnly(2026, 3, 28), 3600d), slices[0]);
        Assert.Equal((new DateOnly(2026, 3, 29), 23 * 3600d), slices[1]);
        Assert.Equal((new DateOnly(2026, 3, 30), 2 * 3600d), slices[2]);
    }

    [Fact]
    public void SplitByDay_FallBackDayHas25Hours()
    {
        // 2026-10-25: clocks go back 03:00 → 02:00. Local day = 10-24 22:00 UTC → 10-25 23:00 UTC.
        var slices = StatisticsPeriods.SplitByDay(Utc(2026, 10, 24, 22, 0), Utc(2026, 10, 25, 23, 0), Amsterdam).ToList();

        Assert.Equal(new[] { (new DateOnly(2026, 10, 25), 25 * 3600d) }, slices);
    }

    [Fact]
    public void SplitByDay_SecondsAlwaysSumToTheInterval()
    {
        var from = Utc(2026, 3, 27, 5, 17);
        var to = Utc(2026, 4, 2, 9, 3);

        Assert.Equal((to - from).TotalSeconds, StatisticsPeriods.SplitByDay(from, to, Amsterdam).Sum(s => s.Seconds), 6);
    }

    [Fact]
    public void SplitByDay_EmptyForAnEmptyOrReversedInterval()
    {
        Assert.Empty(StatisticsPeriods.SplitByDay(Utc(2026, 1, 1, 10), Utc(2026, 1, 1, 10), Amsterdam));
        Assert.Empty(StatisticsPeriods.SplitByDay(Utc(2026, 1, 1, 11), Utc(2026, 1, 1, 10), Amsterdam));
    }

    [Fact]
    public void StartOfDayUtc_IsLocalMidnight()
    {
        Assert.Equal(Utc(2026, 7, 1, 22, 0), StatisticsPeriods.StartOfDayUtc(new DateOnly(2026, 7, 2), Amsterdam));
        Assert.Equal(Utc(2026, 1, 1, 23, 0), StatisticsPeriods.StartOfDayUtc(new DateOnly(2026, 1, 2), Amsterdam));
    }

    [Fact]
    public void FindZone_FallsBackToUtc()
    {
        Assert.Equal(TimeZoneInfo.Utc, StatisticsPeriods.FindZone("Not/AZone"));
        Assert.Equal(TimeZoneInfo.Utc, StatisticsPeriods.FindZone(""));
        Assert.NotEqual(TimeZoneInfo.Utc, Amsterdam);
    }
}
