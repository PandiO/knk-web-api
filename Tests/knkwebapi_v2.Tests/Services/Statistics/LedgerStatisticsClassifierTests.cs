using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Statistics;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Statistics;

/// <summary>DESIGN.md §F.5 economy buckets (L1-5/L1-6) and the D13 coverage guard.</summary>
public class LedgerStatisticsClassifierTests
{
    /// <summary>
    /// The D13 guard (IMPLEMENTATION_PLAN.md §8 link 2, criterion 10): adding a reason code to
    /// CurrencyReasons without deciding its statistics bucket fails here. Decide earned / spent /
    /// excluded in LedgerStatisticsClassifier (and DESIGN.md §F.5) for the new code.
    /// </summary>
    [Fact]
    public void EveryCurrencyReasonCode_IsClassifiedExplicitly()
    {
        var unclassified = CurrencyReasons.All.Select(r => r.Code).Where(c => !LedgerStatisticsClassifier.IsClassified(c)).ToList();

        Assert.True(unclassified.Count == 0, "Reason codes without a statistics bucket: " + string.Join(", ", unclassified));
    }

    [Theory]
    [InlineData("SALARY", LedgerBucket.Earned)]
    [InlineData("SIEGE_REWARD", LedgerBucket.Earned)]
    [InlineData("TITLE_BONUS", LedgerBucket.Earned)]
    [InlineData("DISCOVERY_REWARD", LedgerBucket.Earned)]
    [InlineData("LOOTBOX_REWARD", LedgerBucket.Earned)]
    [InlineData("EVENT_REWARD", LedgerBucket.Earned)]
    [InlineData("KIT_CLAIM_COST", LedgerBucket.Spent)]
    [InlineData("KIT_PURCHASE", LedgerBucket.Spent)]
    [InlineData("LOOTBOX_PURCHASE", LedgerBucket.Spent)]
    [InlineData("TELEPORT_FEE", LedgerBucket.Spent)]
    [InlineData("TRANSFER_FEE", LedgerBucket.Spent)]
    [InlineData("SIGNUP_GRANT", LedgerBucket.Excluded)]
    [InlineData("PLAYER_TRANSFER", LedgerBucket.Excluded)]
    [InlineData("ADMIN_GRANT", LedgerBucket.Excluded)]
    [InlineData("ADMIN_TAKE", LedgerBucket.Excluded)]
    [InlineData("ADMIN_SET", LedgerBucket.Excluded)]
    [InlineData("MERGE_FORFEIT", LedgerBucket.Excluded)]
    [InlineData("MERGE_CARRYOVER", LedgerBucket.Excluded)]
    [InlineData("PREMIUM_TOPUP", LedgerBucket.Excluded)]
    [InlineData("REVERSAL", LedgerBucket.Reversal)]
    public void Classify_FollowsTheDesign(string code, LedgerBucket expected)
    {
        Assert.Equal(expected, LedgerStatisticsClassifier.Classify(code));
    }

    [Theory]
    [InlineData("SOMETHING_NEW")]
    [InlineData("salary")] // codes are exact
    [InlineData(null)]
    public void UnknownCodes_AreExcluded(string? code)
    {
        Assert.Equal(LedgerBucket.Excluded, LedgerStatisticsClassifier.Classify(code));
    }
}
