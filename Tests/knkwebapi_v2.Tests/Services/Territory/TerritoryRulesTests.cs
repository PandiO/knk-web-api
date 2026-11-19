using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Territory;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Territory;

/// <summary>
/// Territory V1 rules (PandiO/knk-workspace#326, territory DESIGN § 1-3, G10, G11): range = base + power × factor, a
/// capital's range multiplied, crown land outranks towns, equal-rank overlaps contest, awards settle them.
/// </summary>
public class TerritoryRulesTests
{
    private static readonly TerritorySettingsDto Smoke = new() { BaseRange = 100, PowerFactor = 2, CapitalMultiplier = 1.5 };

    private static TerritoryHolder Town(int id) => new(TerritoryHolderKind.Town, id);
    private static TerritoryHolder Kingdom(int id) => new(TerritoryHolderKind.Kingdom, id);

    [Fact]
    public void Range_IsBasePlusPowerTimesFactor_AndTheCapitalIsMultiplied()
    {
        // Smoke § 1 step 1: Phoenix power 50 → 200 for normal districts, 300 for the capital.
        Assert.Equal(200, TerritoryRules.DistrictRange(Smoke, 50, isCapital: false));
        Assert.Equal(300, TerritoryRules.DistrictRange(Smoke, 50, isCapital: true));
        Assert.Equal(120, TerritoryRules.TownRange(Smoke, 10));
        Assert.Equal(100, TerritoryRules.TownRange(Smoke, 0));
    }

    [Fact]
    public void Range_IsNeverNegative()
    {
        Assert.Equal(0, TerritoryRules.TownRange(new TerritorySettingsDto { BaseRange = 0, PowerFactor = 1 }, -5));
    }

    [Fact]
    public void CrownLand_OutranksTowns_WithoutAConflict()
    {
        var verdict = TerritoryRules.Resolve(new[] { Town(1), Kingdom(7), Town(2) });

        Assert.Equal(Kingdom(7), verdict.Owner);
        Assert.False(verdict.IsContested);
        Assert.False(TerritoryRules.IsConflictPair(Town(1), Kingdom(7)));
    }

    [Fact]
    public void EqualRank_Overlap_IsContested_AndHasNoOwner()
    {
        var verdict = TerritoryRules.Resolve(new[] { Town(2), Town(1) });

        Assert.Null(verdict.Owner);
        Assert.True(verdict.IsContested);
        Assert.Equal(new[] { Town(1), Town(2) }, verdict.Contesting);
        Assert.True(TerritoryRules.IsConflictPair(Town(1), Town(2)));
        Assert.True(TerritoryRules.IsConflictPair(Kingdom(1), Kingdom(2)));
    }

    [Fact]
    public void OneHolder_TwoRegions_IsNoConflict()
    {
        Assert.Equal(Town(1), TerritoryRules.Resolve(new[] { Town(1), Town(1) }).Owner);
        Assert.False(TerritoryRules.IsConflictPair(Town(1), Town(1)));
        Assert.True(TerritoryRules.Resolve(new TerritoryHolder[0]).IsWilderness);
    }

    [Fact]
    public void AnAward_SettlesTheContest_OnlyForOneOfTheContestants()
    {
        Assert.Equal(Town(1), TerritoryRules.Resolve(new[] { Town(1), Town(2) }, awardedTo: Town(1)).Owner);
        Assert.Null(TerritoryRules.Resolve(new[] { Town(1), Town(2) }, awardedTo: Town(3)).Owner);
    }

    [Fact]
    public void Normalize_PutsTheSmallerHolderFirst()
    {
        Assert.Equal((Town(2), Kingdom(1)), TerritoryRules.Normalize(Kingdom(1), Town(2)));
        Assert.Equal((Town(1), Town(2)), TerritoryRules.Normalize(Town(2), Town(1)));
    }

    [Theory]
    [InlineData(-1, 2, 1.5)]
    [InlineData(100, -1, 1.5)]
    [InlineData(100, 2, 0.5)]
    [InlineData(20000, 2, 1.5)]
    public void Settings_OutOfBounds_AreRefused(int baseRange, double factor, double multiplier)
    {
        Assert.Throws<ArgumentException>(() => TerritoryRules.Validate(
            new TerritorySettingsDto { BaseRange = baseRange, PowerFactor = factor, CapitalMultiplier = multiplier }));
    }

    [Fact]
    public void Chunks_AreDistinctSorted_WithTheirBoundingBox()
    {
        var (chunks, minX, minZ, maxX, maxZ) = TerritoryRules.NormalizeChunks(new[] { new[] { 3, -1 }, new[] { 1, 2 }, new[] { 3, -1 } });

        Assert.Equal(2, chunks.Count);
        Assert.Equal(new[] { 1, 2 }, chunks[0]);
        Assert.Equal((1, -1, 3, 2), (minX, minZ, maxX, maxZ));
        Assert.Throws<ArgumentException>(() => TerritoryRules.NormalizeChunks(new[] { new[] { 1 } }));
    }

    [Fact]
    public void ParseKind_AcceptsTownAndKingdom_IgnoringCase()
    {
        Assert.Equal(TerritoryHolderKind.Kingdom, TerritoryRules.ParseKind("kingdom"));
        Assert.Throws<ArgumentException>(() => TerritoryRules.ParseKind("Province"));
        Assert.Throws<ArgumentException>(() => TerritoryRules.ParseKind("5"));
    }
}
