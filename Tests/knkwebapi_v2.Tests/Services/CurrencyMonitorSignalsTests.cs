using Microsoft.Extensions.Options;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// The in-memory R8/R9 hand-off (currency DESIGN.md §3.9): probing is more than 20 refused
/// transfers per sender within a rolling hour, reported once per hour; cap hits are queued as
/// they happen; the queue is bounded.
/// </summary>
public class CurrencyMonitorSignalsTests
{
    private DateTime _now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    private CurrencyMonitorSignals Signals(int threshold = 20) =>
        new(Options.Create(new CurrencyMonitorOptions { ProbingMaxDeniedPerHour = threshold }), () => _now);

    [Fact]
    public void Probing_FiresWhenTheHourlyCountPassesTheThreshold_OncePerHour()
    {
        var signals = Signals();
        for (var i = 0; i < 20; i++)
        {
            signals.RecordTransferDenial(5, CurrencyErrorCode.DailyCapExceeded);
            _now = _now.AddSeconds(30);
        }
        Assert.Empty(signals.Drain());

        signals.RecordTransferDenial(5, CurrencyErrorCode.InsufficientFunds);
        var draft = Assert.Single(signals.Drain());
        Assert.Equal((CurrencyAlertRules.Probing, 5, "R9:user:5"), (draft.Rule, draft.UserId, draft.DedupKey));
        Assert.Contains("21 refused", draft.Summary);

        // Still probing, same hour: quiet.
        signals.RecordTransferDenial(5, CurrencyErrorCode.InsufficientFunds);
        Assert.Empty(signals.Drain());

        // Another sender is counted separately.
        signals.RecordTransferDenial(6, CurrencyErrorCode.InsufficientFunds);
        Assert.Empty(signals.Drain());
    }

    [Fact]
    public void Probing_OldDenialsFallOutOfTheWindow()
    {
        var signals = Signals(threshold: 3);
        for (var i = 0; i < 3; i++)
        {
            signals.RecordTransferDenial(5, CurrencyErrorCode.CooldownActive);
        }
        _now = _now.AddMinutes(61);
        signals.RecordTransferDenial(5, CurrencyErrorCode.CooldownActive);

        Assert.Empty(signals.Drain());
    }

    [Fact]
    public void CapHits_AreQueued_AndTheQueueIsBounded()
    {
        var signals = Signals();
        signals.RecordCapHit(9, Currency.Coins, "SALARY");

        var draft = Assert.Single(signals.Drain());
        Assert.Equal((CurrencyAlertRules.CapHit, 9, "R8:user:9:Coins:SALARY"), (draft.Rule, draft.UserId, draft.DedupKey));

        for (var i = 0; i < CurrencyMonitorSignals.MaxPending + 50; i++)
        {
            signals.RecordCapHit(i, Currency.Gems, "EVENT_REWARD");
        }
        Assert.Equal(CurrencyMonitorSignals.MaxPending, signals.PendingCount);
    }
}
