using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using static knkwebapi_v2.Tests.Services.CurrencyLedgerSeed;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// The currency monitor's ledger rules (currency-payments DESIGN.md §3.9, IMPLEMENTATION_PLAN.md
/// Phase 5 "each rule on seeded ledgers"): R3–R7 fire on a ledger that crosses their threshold
/// and stay quiet just below it or outside their window, and reconciler mismatches map to R1/R2.
/// The same queries run against MySQL in Tests/MySql/CurrencyMonitorMySqlTests.cs.
/// </summary>
public class CurrencyAnomalyDetectorTests
{
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _db = $"currency-monitor-{Guid.NewGuid()}";

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_db).Options);

    private CurrencyAnomalyDetector Detector(KnKDbContext ctx, CurrencyMonitorOptions? options = null) =>
        new(ctx, Options.Create(options ?? new CurrencyMonitorOptions()));

    private async Task SeedUsersAsync(int count, DateTime createdAt, int firstId = 1)
    {
        await using var ctx = NewContext();
        for (var id = firstId; id < firstId + count; id++)
        {
            ctx.Users.Add(new User { Id = id, Username = $"player{id}", CreatedAt = createdAt });
        }
        await ctx.SaveChangesAsync();
    }

    private async Task SeedAsync(params CurrencyTransaction[] transactions)
    {
        await using var ctx = NewContext();
        await AddAsync(ctx, transactions);
    }

    private async Task<List<CurrencyAlertDraft>> RunAsync(Func<CurrencyAnomalyDetector, Task<List<CurrencyAlertDraft>>> rule)
    {
        await using var ctx = NewContext();
        return await rule(Detector(ctx));
    }

    // ===== R3 funnel =====

    [Fact]
    public async Task Funnel_FiveNewAccountsPayingOnePlayer_IsHigh()
    {
        await SeedUsersAsync(1, Now.AddDays(-400), firstId: 100);          // the receiver, old account
        await SeedUsersAsync(5, Now.AddDays(-2), firstId: 1);              // five fresh alts
        await SeedAsync(Enumerable.Range(1, 5).Select(i => Transfer(i, 100, 1_000, Now.AddHours(-i))).ToArray());

        var draft = Assert.Single(await RunAsync(d => d.FunnelAsync(Now)));

        Assert.Equal((CurrencyAlertRules.Funnel, CurrencyAlertSeverity.High, 100), (draft.Rule, draft.Severity, draft.UserId));
        Assert.Equal("R3:user:100", draft.DedupKey);
        Assert.Contains("player3", draft.Summary);
    }

    [Fact]
    public async Task Funnel_QuietBelowFiveSenders_ForOldAccounts_AndOutsideTheWindow()
    {
        await SeedUsersAsync(1, Now.AddDays(-400), firstId: 100);
        await SeedUsersAsync(4, Now.AddDays(-2), firstId: 1);               // only four fresh accounts
        await SeedUsersAsync(3, Now.AddDays(-30), firstId: 10);             // old friends
        await SeedUsersAsync(1, Now.AddDays(-2), firstId: 20);              // fresh, but paid 2 days ago
        await SeedAsync(
            Transfer(1, 100, 10, Now.AddHours(-1)), Transfer(2, 100, 10, Now.AddHours(-1)), Transfer(2, 100, 10, Now.AddHours(-2)),
            Transfer(3, 100, 10, Now.AddHours(-1)), Transfer(4, 100, 10, Now.AddHours(-1)),
            Transfer(10, 100, 10, Now.AddHours(-1)), Transfer(11, 100, 10, Now.AddHours(-1)), Transfer(12, 100, 10, Now.AddHours(-1)),
            Transfer(20, 100, 10, Now.AddHours(-30)));

        Assert.Empty(await RunAsync(d => d.FunnelAsync(Now)));
    }

    // ===== R4 ping-pong =====

    [Fact]
    public async Task PingPong_ThreeRoundTripsWithinAnHour_IsMedium()
    {
        await SeedUsersAsync(2, Now.AddDays(-100));
        var at = Now.AddMinutes(-50);
        await SeedAsync(
            Transfer(1, 2, 100, at), Transfer(2, 1, 100, at.AddMinutes(5)),
            Transfer(1, 2, 100, at.AddMinutes(10)), Transfer(2, 1, 100, at.AddMinutes(15)),
            Transfer(1, 2, 100, at.AddMinutes(20)), Transfer(2, 1, 100, at.AddMinutes(25)));

        var draft = Assert.Single(await RunAsync(d => d.PingPongAsync(Now)));

        Assert.Equal((CurrencyAlertRules.PingPong, CurrencyAlertSeverity.Medium, "R4:pair:1:2"), (draft.Rule, draft.Severity, draft.DedupKey));
    }

    [Fact]
    public async Task PingPong_OneWayOrOlderThanAnHour_IsQuiet()
    {
        await SeedUsersAsync(3, Now.AddDays(-100));
        await SeedAsync(
            // 1 → 2 five times, 2 → 1 twice: two round trips only
            Transfer(1, 2, 100, Now.AddMinutes(-40)), Transfer(1, 2, 100, Now.AddMinutes(-39)), Transfer(1, 2, 100, Now.AddMinutes(-38)),
            Transfer(1, 2, 100, Now.AddMinutes(-37)), Transfer(1, 2, 100, Now.AddMinutes(-36)),
            Transfer(2, 1, 100, Now.AddMinutes(-30)), Transfer(2, 1, 100, Now.AddMinutes(-29)),
            // 2 ⇄ 3 three times, but two hours ago
            Transfer(2, 3, 100, Now.AddHours(-2)), Transfer(3, 2, 100, Now.AddHours(-2)),
            Transfer(2, 3, 100, Now.AddHours(-2)), Transfer(3, 2, 100, Now.AddHours(-2)),
            Transfer(2, 3, 100, Now.AddHours(-2)), Transfer(3, 2, 100, Now.AddHours(-2)));

        Assert.Empty(await RunAsync(d => d.PingPongAsync(Now)));
    }

    // ===== R5 velocity =====

    [Fact]
    public async Task Velocity_OverHalfAMillionInAnHour_ForAQuietPlayer_IsHigh()
    {
        await SeedUsersAsync(1, Now.AddDays(-100));
        await SeedAsync(
            Tx(CurrencyTransactionKind.Grant, CurrencyReasons.EventReward, Now.AddMinutes(-30), (1, Currency.Coins, 400_000)),
            Tx(CurrencyTransactionKind.Grant, CurrencyReasons.Salary, Now.AddMinutes(-10), (1, Currency.Coins, 200_000)));

        var draft = Assert.Single(await RunAsync(d => d.VelocityAsync(Now)));

        Assert.Equal((CurrencyAlertRules.Velocity, CurrencyAlertSeverity.High, 1), (draft.Rule, draft.Severity, draft.UserId));
        Assert.Contains("600,000", draft.Summary);
    }

    [Fact]
    public async Task Velocity_ScalesWithTheUsualInflow_AndCountsOnlyNetCoins()
    {
        await SeedUsersAsync(2, Now.AddDays(-100));
        // Player 1 usually earns ~100,000 an hour: 10x the mean is ~1,000,000, so 600,000 is normal.
        var history = Enumerable.Range(2, 719).Select(h =>
            Tx(CurrencyTransactionKind.Grant, CurrencyReasons.Salary, Now.AddHours(-h), (1, Currency.Coins, 100_000))).ToArray();
        await SeedAsync(history);
        await SeedAsync(
            Tx(CurrencyTransactionKind.Grant, CurrencyReasons.EventReward, Now.AddMinutes(-30), (1, Currency.Coins, 600_000)),
            // Player 2 received 700,000 but spent 300,000 within the hour: net 400,000.
            Tx(CurrencyTransactionKind.Grant, CurrencyReasons.EventReward, Now.AddMinutes(-30), (2, Currency.Coins, 700_000)),
            Tx(CurrencyTransactionKind.Spend, CurrencyReasons.KitPurchase, Now.AddMinutes(-20), (2, Currency.Coins, -300_000)),
            // Gems and XP aren't coins.
            Tx(CurrencyTransactionKind.Grant, CurrencyReasons.EventReward, Now.AddMinutes(-30), (2, Currency.Experience, 900_000)));

        Assert.Empty(await RunAsync(d => d.VelocityAsync(Now)));
    }

    // ===== R6 staff adjustments =====

    [Fact]
    public async Task Admin_LargeSingleAdjustment_InEitherDirection_OncePerTransaction()
    {
        await SeedUsersAsync(1, Now.AddDays(-100));
        await SeedUsersAsync(1, Now.AddDays(-100), firstId: 900);
        var grant = Tx(CurrencyTransactionKind.AdminAdjust, CurrencyReasons.AdminGrant, Now.AddHours(-3), null, null, 900, (1, Currency.Coins, 1_000_001));
        var take = Tx(CurrencyTransactionKind.AdminAdjust, CurrencyReasons.AdminTake, Now.AddHours(-2), null, null, 900, (1, Currency.Gems, -501));
        var small = Tx(CurrencyTransactionKind.AdminAdjust, CurrencyReasons.AdminGrant, Now.AddHours(-1), null, null, 900, (1, Currency.Coins, 1_000_000));
        var old = Tx(CurrencyTransactionKind.AdminAdjust, CurrencyReasons.AdminGrant, Now.AddHours(-30), null, null, 900, (1, Currency.Coins, 5_000_000));
        var siege = Tx(CurrencyTransactionKind.Grant, CurrencyReasons.SiegeReward, Now.AddHours(-1), (1, Currency.Coins, 5_000_000));
        await SeedAsync(grant, take, small, old, siege);

        var drafts = await RunAsync(d => d.AdminAsync(Now));

        Assert.Equal(2, drafts.Count);
        Assert.All(drafts, d => Assert.Equal((CurrencyAlertRules.Admin, CurrencyAlertSeverity.High, 1), (d.Rule, d.Severity, d.UserId)));
        Assert.Equal(new long?[] { grant.Id, take.Id }, drafts.Select(d => d.TransactionId).OrderBy(id => id));
        Assert.Contains("player900", drafts[0].Summary);
        Assert.Equal($"R6:tx:{grant.Id}:Coins", drafts.Single(d => d.TransactionId == grant.Id).DedupKey);
    }

    [Fact]
    public async Task Admin_MoreThanTenAdjustmentsByOneStaffMemberInAnHour()
    {
        await SeedUsersAsync(1, Now.AddDays(-100));
        await SeedUsersAsync(2, Now.AddDays(-100), firstId: 900);
        await SeedAsync(Enumerable.Range(1, 11).Select(i =>
            Tx(CurrencyTransactionKind.AdminAdjust, CurrencyReasons.AdminGrant, Now.AddMinutes(-i * 5), null, null, 900, (1, Currency.Coins, 10))).ToArray());
        await SeedAsync(Enumerable.Range(1, 10).Select(i =>
            Tx(CurrencyTransactionKind.AdminAdjust, CurrencyReasons.AdminGrant, Now.AddMinutes(-i * 5), null, null, 901, (1, Currency.Coins, 10))).ToArray());

        var draft = Assert.Single(await RunAsync(d => d.AdminAsync(Now)));

        Assert.Equal(("R6:actor:900", 900), (draft.DedupKey, draft.UserId));
        Assert.Contains("11 balance adjustments", draft.Summary);
    }

    // ===== R7 mint rate =====

    [Fact]
    public async Task MintRate_ADayOverThreeTimesTheWeeklyMean_IsMedium()
    {
        await SeedUsersAsync(1, Now.AddDays(-100));
        var baseline = Enumerable.Range(1, 7).Select(day =>
            Tx(CurrencyTransactionKind.Grant, CurrencyReasons.Salary, Now.AddDays(-day).AddHours(-1), (1, Currency.Coins, 20_000))).ToArray();
        await SeedAsync(baseline);
        await SeedAsync(
            Tx(CurrencyTransactionKind.Grant, CurrencyReasons.Salary, Now.AddHours(-2), (1, Currency.Coins, 40_000)),
            Tx(CurrencyTransactionKind.Grant, CurrencyReasons.Salary, Now.AddHours(-1), (1, Currency.Coins, 30_000)));

        var draft = Assert.Single(await RunAsync(d => d.MintRateAsync(Now)));

        Assert.Equal((CurrencyAlertRules.MintRate, CurrencyAlertSeverity.Medium, "R7:SALARY:Coins"), (draft.Rule, draft.Severity, draft.DedupKey));
        Assert.Contains("70,000", draft.Summary);
    }

    [Fact]
    public async Task MintRate_IgnoresTransfersSpendsNewReasonsAndTheNoiseFloor()
    {
        await SeedUsersAsync(2, Now.AddDays(-100));
        await SeedAsync(Enumerable.Range(1, 7).Select(day =>
            Tx(CurrencyTransactionKind.Grant, CurrencyReasons.EventReward, Now.AddDays(-day).AddHours(-1), (1, Currency.Coins, 100))).ToArray());
        await SeedAsync(Enumerable.Range(1, 7).Select(day => Transfer(1, 2, 1_000, Now.AddDays(-day).AddHours(-1))).ToArray());
        await SeedAsync(
            Tx(CurrencyTransactionKind.Grant, CurrencyReasons.EventReward, Now.AddHours(-1), (1, Currency.Coins, 5_000)),     // 50x, under 10,000
            Tx(CurrencyTransactionKind.Grant, CurrencyReasons.DiscoveryReward, Now.AddHours(-1), (1, Currency.Coins, 900_000)), // no history
            Transfer(1, 2, 900_000, Now.AddHours(-1)));

        Assert.Empty(await RunAsync(d => d.MintRateAsync(Now)));
    }

    // ===== R1 / R2 from the reconciler =====

    [Fact]
    public void FromReconciliation_OneR1AndOneR2_WithTheKillSwitchForCoinsAndGemsOnly()
    {
        var mismatches = new List<CurrencyMismatchDto>
        {
            new() { Kind = "BalanceColumn", UserId = 1, Currency = "Coins", Expected = 50, Actual = 100 },
            new() { Kind = "Chain", UserId = 1, Currency = "Experience", Expected = 5, Actual = 7, EntryId = 3, TransactionId = 2 },
            new() { Kind = "UnbalancedTransaction", Currency = "Gems", Expected = 0, Actual = 4, TransactionId = 9 }
        };

        var drafts = CurrencyAnomalyDetector.FromReconciliation(mismatches);

        var r1 = drafts.Single(d => d.Rule == CurrencyAlertRules.Reconciliation);
        Assert.Equal((CurrencyAlertSeverity.Critical, 1), (r1.Severity, r1.UserId));
        Assert.Equal(new[] { Currency.Coins }, r1.DisableTransfersFor);
        var r2 = drafts.Single(d => d.Rule == CurrencyAlertRules.UnbalancedTransaction);
        Assert.Equal((CurrencyAlertSeverity.Critical, 9L), (r2.Severity, r2.TransactionId));
        Assert.Empty(r2.DisableTransfersFor);

        // Same set, same key (re-alerted daily); a changed set, a new key.
        Assert.Equal(r1.DedupKey, CurrencyAnomalyDetector.FromReconciliation(mismatches)[0].DedupKey);
        mismatches[0].Actual = 101;
        Assert.NotEqual(r1.DedupKey, CurrencyAnomalyDetector.FromReconciliation(mismatches)[0].DedupKey);
        Assert.Empty(CurrencyAnomalyDetector.FromReconciliation(new List<CurrencyMismatchDto>()));
    }

    [Fact]
    public async Task EveryRule_IsQuietOnAnEmptyLedger()
    {
        await using var ctx = NewContext();
        var detector = Detector(ctx);

        Assert.Empty(await detector.FunnelAsync(Now));
        Assert.Empty(await detector.PingPongAsync(Now));
        Assert.Empty(await detector.VelocityAsync(Now));
        Assert.Empty(await detector.AdminAsync(Now));
        Assert.Empty(await detector.MintRateAsync(Now));
    }
}
