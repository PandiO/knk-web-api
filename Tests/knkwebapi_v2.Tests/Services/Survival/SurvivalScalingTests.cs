using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Survival;

/// <summary>DESIGN §7 presets (S29), per-stat weights (S54), per-player scaling (S55), caps and the §11 preview.</summary>
public class SurvivalScalingTests
{
    [Theory]
    [InlineData(SurvivalScalingPreset.Linear, 1, 6.0)]
    [InlineData(SurvivalScalingPreset.Linear, 4, 12.0)]
    [InlineData(SurvivalScalingPreset.Logarithmic, 1, 6.0)]
    [InlineData(SurvivalScalingPreset.Exponential, 1, 6.0)]
    [InlineData(SurvivalScalingPreset.Exponential, 3, 7.26)]
    [InlineData(SurvivalScalingPreset.Hybrid, 1, 6.0)]
    [InlineData(SurvivalScalingPreset.Hybrid, 3, 10.0)]   // linear up to the switch round 3
    [InlineData(SurvivalScalingPreset.Hybrid, 5, 12.1)]   // (6 + 2·2) · 1.1²
    public void CurveValue_FollowsThePresetsTable(SurvivalScalingPreset preset, int round, double expected)
    {
        var value = SurvivalScaling.CurveValue(preset, b: 6, a: 2, m: 1.1, s: 3, round);
        Assert.Equal(expected, value, 6);
    }

    [Fact]
    public void Logarithmic_GrowsWithLn()
    {
        Assert.Equal(6 + 2 * Math.Log(5), SurvivalScaling.CurveValue(SurvivalScalingPreset.Logarithmic, 6, 2, 1, 1, 5), 9);
    }

    [Fact]
    public void Round1_IsVanilla_ForEveryPreset_AndEnemyCountIsTheBase()
    {
        foreach (var preset in Enum.GetValues<SurvivalScalingPreset>())
        {
            var profile = SurvivalTestData.Profile();
            profile.StatPreset = preset;
            profile.CountPreset = preset;
            Assert.Equal(1.0, SurvivalScaling.StatMultiplier(profile, 1, 1, 1));
            Assert.Equal(6, SurvivalScaling.EnemyCount(profile, 1, 1));
        }
    }

    [Fact]
    public void StatWeight_ScalesTheGrowthOnly()
    {
        var profile = SurvivalTestData.Profile();
        profile.StatPreset = SurvivalScalingPreset.Linear;
        profile.StatStep = 0.5; // curve(3) = 2
        profile.StatCap = 100;
        Assert.Equal(2.0, SurvivalScaling.StatMultiplier(profile, 1, 3, 1), 9);
        Assert.Equal(1.3, SurvivalScaling.StatMultiplier(profile, 0.3, 3, 1), 9);
        Assert.Equal(1.0, SurvivalScaling.StatMultiplier(profile, 0, 3, 1), 9);
    }

    [Fact]
    public void PerPlayer_ScalesTheCountAlways_AndTheStatsOnlyWhenEnabled()
    {
        var profile = SurvivalTestData.Profile(); // base 6, p = 0.25
        Assert.Equal(6, SurvivalScaling.EnemyCount(profile, 1, 1));
        Assert.Equal(10, SurvivalScaling.EnemyCount(profile, 1, 4)); // 6 × 1.75 = 10.5 → 10
        Assert.Equal(1.0, SurvivalScaling.StatMultiplier(profile, 1, 1, 4));
        profile.PerPlayerAppliesToStats = true;
        profile.StatCap = 100;
        Assert.Equal(1.75, SurvivalScaling.StatMultiplier(profile, 1, 1, 4), 9);
    }

    [Fact]
    public void Caps_ApplyLast_AndTheCountNeverDropsBelowOne()
    {
        var profile = SurvivalTestData.Profile();
        profile.CountCap = 8;
        Assert.Equal(8, SurvivalScaling.EnemyCount(profile, 10, 1));
        profile.CountCap = 0;
        profile.CountStep = -5;
        Assert.Equal(1, SurvivalScaling.EnemyCount(profile, 10, 1));

        profile.StatPreset = SurvivalScalingPreset.Exponential;
        profile.StatMultiplier = 2;
        profile.StatCap = 1.5;
        Assert.Equal(1.5, SurvivalScaling.StatMultiplier(profile, 1, 20, 1));
        profile.StatStep = -3;
        profile.StatPreset = SurvivalScalingPreset.Linear;
        Assert.Equal(0, SurvivalScaling.StatMultiplier(profile, 1, 5, 1));
    }

    [Fact]
    public void Preview_ListsEveryRound_WithCountAndEveryStat()
    {
        var profile = SurvivalTestData.Profile();
        var preview = SurvivalScaling.Preview(profile, 1, 5, 2);

        Assert.Equal(5, preview.Rounds.Count);
        Assert.Equal(2, preview.Players);
        Assert.Equal(1, preview.Rounds[0].Round);
        Assert.Equal(7, preview.Rounds[0].EnemyCount); // 6 × 1.25
        Assert.Equal(1.0, preview.Rounds[0].Health);
        Assert.Equal(1.0, preview.Rounds[0].WalkingSpeed);
        Assert.True(preview.Rounds[4].Health > preview.Rounds[0].Health);
        Assert.True(preview.Rounds[4].WalkingSpeed < preview.Rounds[4].Health); // weight 0.3 vs 1
        Assert.All(preview.Rounds, r => Assert.True(r.Health <= profile.StatCap));
    }

    [Theory]
    [InlineData(0, 5, 1)]
    [InlineData(5, 4, 1)]
    [InlineData(1, 600, 1)]
    [InlineData(1, 5, 0)]
    public void Preview_RefusesBadRanges(int from, int to, int players)
    {
        Assert.Throws<ArgumentException>(() => SurvivalScaling.Preview(SurvivalTestData.Profile(), from, to, players));
    }

    [Fact]
    public void MaxWalkingSpeedMultiplier_IsTheStatCapWhenTheWeightedCurveGrows_Else1()
    {
        var profile = SurvivalTestData.Profile(); // hybrid growing, weight 0.3, cap 1.5
        Assert.Equal(1.5, SurvivalScaling.MaxWalkingSpeedMultiplier(profile, 20));
        profile.WalkingSpeedWeight = 0;
        Assert.Equal(1.0, SurvivalScaling.MaxWalkingSpeedMultiplier(profile, 20));
        profile.PerPlayerAppliesToStats = true; // 1 + 0.25 × 19 = 5.75, capped at 1.5
        Assert.Equal(1.5, SurvivalScaling.MaxWalkingSpeedMultiplier(profile, 20));
        Assert.Equal(1.0, SurvivalScaling.MaxWalkingSpeedMultiplier(profile, 1));
    }

    [Theory]
    [InlineData("60.5", 6050)]
    [InlineData("100", 10000)]
    [InlineData("0.01", 1)]
    [InlineData("0", 0)]
    public void ToBasisPoints_IsExactAt0_01Percent(string percent, int expected)
    {
        Assert.Equal(expected, SurvivalScaling.ToBasisPoints(decimal.Parse(percent, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("0.001")]
    [InlineData("33.333")]
    [InlineData("100.01")]
    [InlineData("-1")]
    public void ToBasisPoints_RejectsFinerOrOutOfRangeValues(string percent)
    {
        Assert.Throws<ArgumentException>(() => SurvivalScaling.ToBasisPoints(decimal.Parse(percent, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void VanillaWalkingSpeed_KnowsTheCommonTypes()
    {
        Assert.Equal(0.23, SurvivalScaling.VanillaWalkingSpeed("ZOMBIE"));
        Assert.Equal(0.3, SurvivalScaling.VanillaWalkingSpeed("spider"));
        Assert.Equal(0.25, SurvivalScaling.VanillaWalkingSpeed("SOMETHING_NEW"));
    }
}
