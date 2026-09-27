using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// The Knk.Currency meter (currency DESIGN.md §3.9): postings, amounts, replays, denials (once per
/// refusal however deep the call chain) and lock waits, and a refusal at the balance cap reported
/// for alert R8.
/// </summary>
public class CurrencyMetricsTests : IDisposable
{
    private readonly string _db = $"currency-metrics-{Guid.NewGuid()}";
    private readonly CurrencyMetrics _metrics = new();
    private readonly CurrencyMonitorSignals _signals = new();
    private readonly MeterListener _listener = new();
    private readonly List<(string Instrument, double Value, Dictionary<string, object?> Tags)> _measurements = new();

    public CurrencyMetricsTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter, _metrics.Meter))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((i, v, tags, _) => Record(i, v, tags));
        _listener.SetMeasurementEventCallback<double>((i, v, tags, _) => Record(i, v, tags));
        _listener.Start();
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var tag in tags) dict[tag.Key] = tag.Value;
        lock (_measurements) _measurements.Add((instrument.Name, value, dict));
    }

    public void Dispose()
    {
        _listener.Dispose();
        _metrics.Dispose();
    }

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_db).Options);

    private CurrencyService Service(KnKDbContext ctx) =>
        new(new CurrencyRepository(ctx), new UserRepository(ctx), NullLogger<CurrencyService>.Instance, null, _metrics, _signals);

    private async Task SeedAsync(int coins)
    {
        await using var ctx = NewContext();
        ctx.Users.Add(new User { Id = 1, Username = "alice", Coins = coins });
        await ctx.SaveChangesAsync();
    }

    private List<(double Value, Dictionary<string, object?> Tags)> Of(string instrument) =>
        _measurements.Where(m => m.Instrument == instrument).Select(m => (m.Value, m.Tags)).ToList();

    [Fact]
    public async Task APosting_CountsOnce_WithItsAmount_AndARetryCountsAsAReplay()
    {
        await SeedAsync(100);
        await using var ctx = NewContext();
        var context = CurrencyContext.ForSystem("Test", CurrencyReasons.EventReward, "event:1:1");

        await Service(ctx).GrantAsync(1, Currency.Coins, 250, context);
        await Service(ctx).GrantAsync(1, Currency.Coins, 250, context);

        var posting = Assert.Single(Of("knk.currency.postings"));
        Assert.Equal(("EVENT_REWARD", "Coins", "Grant"), ((string)posting.Tags["reason"]!, (string)posting.Tags["currency"]!, (string)posting.Tags["kind"]!));
        Assert.Equal(250, Assert.Single(Of("knk.currency.amount")).Value);
        Assert.Equal("EVENT_REWARD", Assert.Single(Of("knk.currency.replays")).Tags["reason"]);
        Assert.Equal(2, Of("knk.currency.lock_wait_ms").Count);
        Assert.Empty(Of("knk.currency.denials"));
    }

    [Fact]
    public async Task ARefusal_CountsOnceByCode_AndACapHitIsReportedForR8()
    {
        await SeedAsync(999_999_000);
        await using var ctx = NewContext();

        var funds = await Assert.ThrowsAsync<CurrencyException>(() =>
            Service(ctx).SpendAsync(1, Currency.Gems, 5, CurrencyContext.ForSystem("Test", CurrencyReasons.KitPurchase, "kit-purchase:1:1")));
        var cap = await Assert.ThrowsAsync<CurrencyException>(() =>
            Service(ctx).GrantAsync(1, Currency.Coins, 5_000, CurrencyContext.ForSystem("Test", CurrencyReasons.Salary, "salary:1:x")));

        Assert.Equal((CurrencyErrorCode.InsufficientFunds, CurrencyErrorCode.BalanceCapExceeded), (funds.Code, cap.Code));
        Assert.Equal(new[] { "InsufficientFunds", "BalanceCapExceeded" }, Of("knk.currency.denials").Select(d => (string)d.Tags["code"]!));
        Assert.Empty(Of("knk.currency.postings"));

        var r8 = Assert.Single(_signals.Drain());
        Assert.Equal((CurrencyAlertRules.CapHit, 1, "R8:user:1:Coins:SALARY"), (r8.Rule, r8.UserId, r8.DedupKey));
    }

    [Fact]
    public void TheReconciliationGauge_ReportsNothingBeforeTheFirstRun()
    {
        _listener.RecordObservableInstruments();
        Assert.Empty(Of("knk.currency.reconciliation.mismatches"));

        _metrics.RecordReconciliation(3, TimeSpan.FromMilliseconds(12));
        _listener.RecordObservableInstruments();

        Assert.Equal(3, Assert.Single(Of("knk.currency.reconciliation.mismatches")).Value);
        Assert.Equal(12, Assert.Single(Of("knk.currency.reconciliation.duration_ms")).Value);
    }
}
