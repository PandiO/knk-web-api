using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Statistics;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Statistics;

/// <summary>DESIGN.md §F.5 economy buckets: every gain is earned, every loss spent (D16, 2026-10-03).</summary>
public class LedgerStatisticsClassifierTests
{
    /// <summary>Every reason code counts — gameplay, transfers, staff, signup, merges, premium.</summary>
    [Fact]
    public void EveryCurrencyReasonCode_CountsBySign()
    {
        foreach (var code in CurrencyReasons.All.Select(r => r.Code).Where(c => c != CurrencyReasons.Reversal))
        {
            Assert.Equal(LedgerBucket.Earned, LedgerStatisticsClassifier.Classify(code, 10));
            Assert.Equal(LedgerBucket.Spent, LedgerStatisticsClassifier.Classify(code, -10));
        }
    }

    [Theory]
    [InlineData("PLAYER_TRANSFER", 20, LedgerBucket.Earned)]   // received
    [InlineData("PLAYER_TRANSFER", -20, LedgerBucket.Spent)]   // sent
    [InlineData("ADMIN_GRANT", 5, LedgerBucket.Earned)]
    [InlineData("ADMIN_TAKE", -5, LedgerBucket.Spent)]
    [InlineData("ADMIN_SET", 7, LedgerBucket.Earned)]
    [InlineData("ADMIN_SET", -7, LedgerBucket.Spent)]
    [InlineData("SIGNUP_GRANT", 100, LedgerBucket.Earned)]
    [InlineData("MERGE_FORFEIT", -50, LedgerBucket.Spent)]
    [InlineData("MERGE_CARRYOVER", 50, LedgerBucket.Earned)]
    [InlineData("PREMIUM_TOPUP", 30, LedgerBucket.Earned)]
    [InlineData("SOMETHING_NEW", 1, LedgerBucket.Earned)]
    [InlineData(null, -1, LedgerBucket.Spent)]
    public void Classify_BySign(string? code, long amount, LedgerBucket expected)
    {
        Assert.Equal(expected, LedgerStatisticsClassifier.Classify(code, amount));
    }

    /// <summary>A reversal lowers what it reverses: −amount undoes a gain, +amount undoes a loss.</summary>
    [Theory]
    [InlineData(-30, LedgerBucket.Earned)]
    [InlineData(30, LedgerBucket.Spent)]
    public void Reversal_LowersTheReversedBucket(long amount, LedgerBucket expected)
    {
        Assert.Equal(expected, LedgerStatisticsClassifier.Classify(CurrencyReasons.Reversal, amount));
    }

    /// <summary>A reversal of a reversal restores the original: its bucket follows the chain depth.</summary>
    [Theory]
    [InlineData(-30, 2, LedgerBucket.Spent)]  // re-applies a reversed loss → spent goes up again
    [InlineData(30, 2, LedgerBucket.Earned)]  // re-applies a reversed gain → earned goes up again
    [InlineData(-30, 3, LedgerBucket.Earned)]
    public void ReversalChains_FollowTheOriginal(long amount, int depth, LedgerBucket expected)
    {
        Assert.Equal(expected, LedgerStatisticsClassifier.Classify(CurrencyReasons.Reversal, amount, depth));
    }

    [Fact]
    public void Reversal_WithUnresolvedOrigin_IsExcluded()
    {
        Assert.Equal(LedgerBucket.Excluded, LedgerStatisticsClassifier.Classify(CurrencyReasons.Reversal, 30, null));
    }

    [Theory]
    [InlineData("SALARY")]
    [InlineData("REVERSAL")]
    public void ZeroLegs_ChangeNothing(string code)
    {
        Assert.Equal(LedgerBucket.Excluded, LedgerStatisticsClassifier.Classify(code, 0));
    }

    [Theory]
    [InlineData(LedgerBucket.Earned, 20, 20)]   // gain → earned + 20
    [InlineData(LedgerBucket.Earned, -20, -20)] // reversed gain → earned − 20
    [InlineData(LedgerBucket.Spent, -20, 20)]   // loss → spent + 20
    [InlineData(LedgerBucket.Spent, 20, -20)]   // reversed loss → spent − 20
    public void MetricFor_Coins(LedgerBucket bucket, long amount, decimal expected)
    {
        var (metric, value) = LedgerStatisticsProjector.MetricFor(bucket, knkwebapi_v2.Enums.Currency.Coins, amount);

        Assert.Equal(bucket == LedgerBucket.Earned ? StatisticsCatalog.CoinsEarned : StatisticsCatalog.CoinsSpent, metric);
        Assert.Equal(expected, value);
    }
}
