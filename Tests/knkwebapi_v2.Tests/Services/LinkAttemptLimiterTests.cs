using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Services;

/// <summary>KNG-208: Security:LinkAttempts, 5 failed codes per account in 15 minutes → 15 minutes blocked.</summary>
public class LinkAttemptLimiterTests
{
    private static LinkAttemptLimiter Limiter(int maxFailures = 5, int lockMinutes = 15) =>
        new(new MemoryCache(new MemoryCacheOptions()), Options.Create(new SecuritySettings
        {
            LinkAttempts = new LockoutSettings { MaxFailures = maxFailures, WindowMinutes = 15, LockMinutes = lockMinutes }
        }));

    [Fact]
    public void Defaults_AreFiveFailuresInFifteenMinutesAndAFifteenMinuteBlock()
    {
        var settings = new SecuritySettings().LinkAttempts;
        Assert.Equal((5, 15, 15), (settings.MaxFailures, settings.WindowMinutes, settings.LockMinutes));
    }

    [Fact]
    public void TheFifthFailureLocks_ForTheConfiguredMinutes()
    {
        var limiter = Limiter();

        for (var i = 1; i <= 4; i++)
        {
            Assert.False(limiter.RecordFailure(7), $"failure {i}");
            Assert.False(limiter.IsLocked(7, out _));
        }

        Assert.True(limiter.RecordFailure(7));
        Assert.True(limiter.IsLocked(7, out var remaining));
        Assert.InRange(remaining, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void OtherAccountsAreNotAffected()
    {
        var limiter = Limiter(maxFailures: 1);
        limiter.RecordFailure(7);

        Assert.True(limiter.IsLocked(7, out _));
        Assert.False(limiter.IsLocked(8, out _));
    }

    [Fact]
    public void ResetClearsTheCountAndTheBlock()
    {
        var limiter = Limiter(maxFailures: 2);
        limiter.RecordFailure(7);
        limiter.Reset(7);
        Assert.False(limiter.RecordFailure(7)); // the count started over

        limiter.RecordFailure(7);
        Assert.True(limiter.IsLocked(7, out _));
        limiter.Reset(7);
        Assert.False(limiter.IsLocked(7, out _));
    }

    [Fact]
    public void TheExceptionCarriesThePlayerFacingMessage()
    {
        var ex = new LinkAttemptsExceededException(TimeSpan.FromMinutes(9.2));
        Assert.Equal("Too many attempts. Try again in 10 minutes.", ex.Message);
    }
}
