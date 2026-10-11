using System.Globalization;
using knkwebapi_v2.Services.Statistics;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Statistics;

/// <summary>DESIGN.md §F.10 display rounding, applied to final totals only.</summary>
public class StatisticsFormattingTests
{
    [Theory]
    [InlineData("damage_dealt.player", "12.5", "13")] // half away from zero
    [InlineData("damage_dealt.player", "12.49", "12")]
    [InlineData("gate_damage", "0.5", "1")]
    [InlineData("gate_damage", "-2.5", "-3")]
    [InlineData("distance.foot", "99.99", "99")] // floor
    [InlineData("distance.vehicle", "100", "100")]
    [InlineData("highest_fall", "23.45", "23.5")] // one decimal
    [InlineData("highest_fall", "23.44", "23.4")]
    [InlineData("active_playtime", "3599.9", "3599")] // whole seconds
    [InlineData("pvp_kills", "3", "3")]
    public void Display(string metric, string raw, string expected)
    {
        // Invariant: on a machine with a comma decimal separator "23.45" would parse as 2345.
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture),
            StatisticsFormatting.Display(metric, decimal.Parse(raw, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Display_UnknownMetricThrows()
    {
        Assert.Throws<ArgumentException>(() => StatisticsFormatting.Display("nope", 1m));
    }
}
