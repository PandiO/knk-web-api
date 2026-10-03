using knkwebapi_v2.Configuration;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Statistics;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Statistics;

/// <summary>
/// Ledger projection (DESIGN.md §F.5, D9, D13; link 2 acceptance criterion 4): buckets incl.
/// transfers and reversals, xp_gained, title promotions/demotions incl. merges, idempotency by
/// cursor, and a rebuild reproducing identical rows.
/// </summary>
public class LedgerStatisticsProjectorTests : IDisposable
{
    private readonly StatisticsTestDb _db = new();
    private long _nextTx = 1;

    public LedgerStatisticsProjectorTests()
    {
        _db.Context.TitleBrackets.AddRange(
            new TitleBracket { Id = 10, MaleName = "Serf", FemaleName = "Serf", MinExperience = 0 },
            new TitleBracket { Id = 11, MaleName = "Peasant", FemaleName = "Peasant woman", MinExperience = 100 },
            new TitleBracket { Id = 12, MaleName = "Yeoman", FemaleName = "Yeowoman", MinExperience = 500 });
        _db.Context.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static DateTime At(int day, int hour) => new(2026, 10, day, hour, 0, 0, DateTimeKind.Utc);

    /// <summary>One ledger transaction with user legs (amount, balance before) and the system leg.</summary>
    private CurrencyTransaction Tx(string reason, DateTime at, long? reverses, params (int UserId, Currency Currency, long Amount, long Before)[] legs)
    {
        var tx = new CurrencyTransaction
        {
            Id = _nextTx++,
            PublicId = CurrencyIds.NewPublicId(),
            Kind = CurrencyTransactionKind.Grant,
            ReasonCode = reason,
            Reason = "test",
            IdempotencyScope = "test",
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            RequestHash = new string('0', 64),
            Initiator = CurrencyInitiator.System,
            ReversesTransactionId = reverses,
            CreatedAt = at
        };
        foreach (var leg in legs)
        {
            tx.Entries.Add(new CurrencyEntry
            {
                Currency = leg.Currency, AccountKind = CurrencyAccountKind.User, UserId = leg.UserId,
                Operation = leg.Amount < 0 ? CurrencyOperation.Remove : CurrencyOperation.Add,
                Amount = leg.Amount, BalanceBefore = leg.Before, BalanceAfter = leg.Before + leg.Amount
            });
        }
        foreach (var group in legs.GroupBy(l => l.Currency).Where(g => g.Sum(l => l.Amount) != 0))
        {
            tx.Entries.Add(new CurrencyEntry
            {
                Currency = group.Key, AccountKind = CurrencyAccountKind.System, SystemAccount = "SYS_TEST",
                Operation = CurrencyOperation.Add, Amount = -group.Sum(l => l.Amount)
            });
        }
        _db.Context.CurrencyTransactions.Add(tx);
        _db.Context.SaveChanges();
        return tx;
    }

    private async Task<int> DrainAsync()
    {
        var total = 0;
        int count;
        while ((count = await _db.LedgerProjector().ProjectNextAsync()) > 0) total += count;
        return total;
    }

    private void SeedEconomy()
    {
        Tx(CurrencyReasons.Salary, At(1, 10), null, (1, Currency.Coins, 100, 0));
        Tx(CurrencyReasons.DiscoveryReward, At(1, 11), null, (1, Currency.Gems, 5, 0), (1, Currency.Experience, 40, 0));
        Tx(CurrencyReasons.SiegeReward, At(2, 10), null, (1, Currency.Experience, 80, 40));      // Serf → Peasant (120)
        var kit = Tx(CurrencyReasons.KitPurchase, At(2, 11), null, (1, Currency.Coins, -30, 100));
        Tx(CurrencyReasons.TeleportFee, At(2, 12), null, (1, Currency.Gems, -2, 5));
        Tx(CurrencyReasons.PlayerTransfer, At(2, 13), null, (1, Currency.Coins, -20, 70), (2, Currency.Coins, 20, 0));
        Tx(CurrencyReasons.TransferFee, At(2, 13), null, (1, Currency.Coins, -1, 50));
        Tx(CurrencyReasons.AdminGrant, At(2, 14), null, (1, Currency.Coins, 1000, 49), (1, Currency.Experience, 500, 120)); // → Yeoman (620)
        Tx(CurrencyReasons.SignupGrant, At(2, 14), null, (2, Currency.Coins, 250, 20));
        Tx(CurrencyReasons.Reversal, At(3, 9), kit.Id, (1, Currency.Coins, 30, 1049));          // the kit is refunded
        Tx(CurrencyReasons.AdminTake, At(3, 10), null, (1, Currency.Experience, -560, 620));     // Yeoman → Serf (60)
    }

    [Fact]
    public async Task Buckets_CountEveryGainAndLoss()
    {
        SeedEconomy();

        Assert.Equal(14, await DrainAsync()); // user legs only

        // D16 (2026-10-03): every way of gaining/losing counts — transfers, staff, signup included.
        Assert.Equal(1100m, _db.Total(1, "coins_earned"));                 // salary 100 + admin grant 1000
        Assert.Equal(21m, _db.Total(1, "coins_spent"));                    // kit 30 + transfer 20 + fee 1 − kit reversal 30
        Assert.Equal(5m, _db.Total(1, "gems_earned"));
        Assert.Equal(2m, _db.Total(1, "gems_spent"));
        Assert.Equal(620m, _db.Total(1, "xp_gained"));                     // discovery 40 + siege 80 + admin 500; XP losses are no statistic
        Assert.Equal(270m, _db.Total(2, "coins_earned"));                  // transfer received 20 + signup grant 250

        // Periods allocate by the transaction's day; the reversal lands on its own day.
        Assert.Equal(new[] { (new DateOnly(2026, 10, 2), 51m), (new DateOnly(2026, 10, 3), -30m) },
            _db.Daily(1, "coins_spent").Select(d => (d.Day, d.Value)));
    }

    [Fact]
    public async Task TitleChanges_AreProjectedFromXpLegs_PromotionsAndDemotions()
    {
        SeedEconomy();
        await DrainAsync();

        var changes = _db.Context.PlayerTitleChanges.AsNoTracking().OrderBy(c => c.ChangedAt).ToList();
        Assert.Equal(new[]
        {
            ("Serf", "Peasant", TitleChangeDirection.Promotion, 40L, 120L),
            ("Peasant", "Yeoman", TitleChangeDirection.Promotion, 120L, 620L),
            ("Yeoman", "Serf", TitleChangeDirection.Demotion, 620L, 60L),
        }, changes.Select(c => (c.FromTitleName!, c.ToTitleName, c.Direction, c.ExperienceBefore, c.ExperienceAfter)));
        Assert.All(changes, c => Assert.Equal(1, c.UserId));
        Assert.Equal(At(2, 10), changes[0].ChangedAt);
    }

    [Fact]
    public async Task TitleNames_AreGendered_AndMergesCountToo()
    {
        Tx(CurrencyReasons.EventReward, At(1, 10), null, (3, Currency.Experience, 150, 0));                       // carol → Peasant woman
        Tx(CurrencyReasons.MergeForfeit, At(2, 10), null, (3, Currency.Experience, -150, 150));                   // secondary zeroed
        Tx(CurrencyReasons.MergeCarryover, At(2, 10), null, (1, Currency.Experience, 600, 0));                    // primary → Yeoman

        await DrainAsync();

        var changes = _db.Context.PlayerTitleChanges.AsNoTracking().OrderBy(c => c.Id).ToList();
        Assert.Equal(new[]
        {
            (3, "Serf", "Peasant woman", TitleChangeDirection.Promotion),
            (3, "Peasant woman", "Serf", TitleChangeDirection.Demotion),
            (1, "Serf", "Yeoman", TitleChangeDirection.Promotion),
        }, changes.Select(c => (c.UserId, c.FromTitleName!, c.ToTitleName, c.Direction)));
        Assert.Equal(600m, _db.Total(1, "xp_gained")); // D16: a merge carryover is a gain too
        Assert.Equal(150m, _db.Total(3, "xp_gained")); // the forfeit is an XP loss: no statistic
    }

    [Fact]
    public async Task ReRunning_IsIdempotent_TheCursorAdvances()
    {
        SeedEconomy();
        await DrainAsync();

        Assert.Equal(0, await DrainAsync());
        Assert.Equal(1100m, _db.Total(1, "coins_earned"));
        Assert.Equal(3, _db.Context.PlayerTitleChanges.Count());

        Tx(CurrencyReasons.Salary, At(3, 11), null, (1, Currency.Coins, 50, 1079));
        Assert.Equal(1, await DrainAsync());
        Assert.Equal(1150m, _db.Total(1, "coins_earned"));
        var lastUserLeg = _db.Context.CurrencyEntries.Where(e => e.AccountKind == CurrencyAccountKind.User).Max(e => e.Id);
        Assert.Equal(lastUserLeg, _db.Context.StatisticsProjectionCursors.AsNoTracking().Single().LastSourceId);
    }

    [Fact]
    public async Task YoungLegs_WaitForTheSafetyLag_AndTheCursorNeverPassesThem()
    {
        using var db = new StatisticsTestDb(new StatisticsOptions { ProjectionSafetyLagSeconds = 60 });
        var projector = db.LedgerProjector();
        db.Context.CurrencyTransactions.Add(LegTx(1, StatisticsTestDb.Now.AddMinutes(-5)));
        db.Context.CurrencyTransactions.Add(LegTx(2, StatisticsTestDb.Now.AddSeconds(-10)));  // too young
        db.Context.CurrencyTransactions.Add(LegTx(3, StatisticsTestDb.Now.AddMinutes(-4)));   // older, but after the young one
        db.Context.SaveChanges();

        Assert.Equal(1, await projector.ProjectNextAsync());
        Assert.Equal(0, await projector.ProjectNextAsync());
        Assert.Equal(10m, db.Total(1, "coins_earned"));

        db.Time.UtcNow = StatisticsTestDb.Now.AddMinutes(2);
        Assert.Equal(2, await projector.ProjectNextAsync());
        Assert.Equal(30m, db.Total(1, "coins_earned"));

        static CurrencyTransaction LegTx(long id, DateTime at) => new()
        {
            Id = id, PublicId = CurrencyIds.NewPublicId(), Kind = CurrencyTransactionKind.Grant, ReasonCode = CurrencyReasons.Salary,
            Reason = "t", IdempotencyScope = "t", IdempotencyKey = "k" + id, RequestHash = new string('0', 64), CreatedAt = at,
            Entries = { new CurrencyEntry { Id = id, Currency = Currency.Coins, AccountKind = CurrencyAccountKind.User, UserId = 1, Amount = 10, BalanceBefore = 0, BalanceAfter = 10 } }
        };
    }

    [Fact]
    public async Task ReversalOfAReversal_TakesTheOriginalBucketAgain()
    {
        var kit = Tx(CurrencyReasons.KitPurchase, At(1, 10), null, (1, Currency.Coins, -40, 100));
        var refund = Tx(CurrencyReasons.Reversal, At(1, 11), kit.Id, (1, Currency.Coins, 40, 60));
        Tx(CurrencyReasons.Reversal, At(1, 12), refund.Id, (1, Currency.Coins, -40, 100));

        await DrainAsync();

        Assert.Equal(40m, _db.Total(1, "coins_spent"));
    }

    [Fact]
    public async Task RebuildAll_ReproducesIdenticalRows()
    {
        SeedEconomy();
        await DrainAsync();
        var before = Snapshot();

        await _db.LedgerProjector().RebuildAsync(null);
        Assert.Empty(_db.Context.PlayerTitleChanges.AsNoTracking());
        Assert.Equal(0, _db.Context.StatisticsProjectionCursors.AsNoTracking().Single().LastSourceId);
        await DrainAsync();

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task RebuildOneUser_ReproducesTheirRows_AndLeavesOthersAlone()
    {
        SeedEconomy();
        Tx(CurrencyReasons.Salary, At(3, 11), null, (2, Currency.Coins, 7, 270));
        await DrainAsync();
        var before = Snapshot();

        Assert.Equal(12, await _db.LedgerProjector().RebuildAsync(1));

        Assert.Equal(before, Snapshot());
        Assert.Equal(277m, _db.Total(2, "coins_earned")); // transfer 20 + signup 250 + salary 7 (D16)
    }

    /// <summary>GDPR-erased accounts never get statistics again — not from new legs, not from a
    /// rebuild of the kept ledger (developer decision 2026-10-03, DESIGN.md §F.14).</summary>
    [Fact]
    public async Task ErasedAccounts_GetNothing_FromProjectionOrRebuild()
    {
        SeedEconomy();
        var user = _db.Context.Users.Single(u => u.Id == 1);
        user.DeletedReason = PrivacyErasure.Reason;
        user.IsActive = false;
        _db.Context.SaveChanges();

        await DrainAsync();
        Assert.Null(_db.Total(1, "coins_earned"));
        Assert.Empty(_db.Context.PlayerTitleChanges.AsNoTracking());
        Assert.Equal(270m, _db.Total(2, "coins_earned")); // others are unaffected

        await _db.LedgerProjector().RebuildAsync(null);
        await DrainAsync();
        await _db.LedgerProjector().RebuildAsync(1);

        Assert.Null(_db.Total(1, "coins_earned"));
        Assert.Null(_db.Total(1, "xp_gained"));
        Assert.Empty(_db.Context.PlayerTitleChanges.AsNoTracking());
        Assert.Equal(270m, _db.Total(2, "coins_earned"));
    }

    [Fact]
    public async Task PluginStatistics_AreNotTouchedByARebuild()
    {
        _db.Context.PlayerStatTotals.Add(new PlayerStatTotal { UserId = 1, MetricKey = "pve_kills", ContextKey = "open_world", Value = 4 });
        _db.Context.SaveChanges();
        SeedEconomy();
        await DrainAsync();

        await _db.LedgerProjector().RebuildAsync(null);

        Assert.Equal(4m, _db.Total(1, "pve_kills", "open_world"));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(99, 10)]
    [InlineData(100, 11)]
    [InlineData(499, 11)]
    [InlineData(500, 12)]
    [InlineData(100_000, 12)]
    public void BracketFor_MatchesTitleService(long xp, int bracketId)
    {
        var brackets = _db.Context.TitleBrackets.OrderBy(b => b.MinExperience).ToList();
        Assert.Equal(bracketId, LedgerStatisticsProjector.BracketFor(brackets, xp)!.Id);
    }

    private List<string> Snapshot() =>
        _db.Context.PlayerStatDailies.AsNoTracking().AsEnumerable()
            .Select(d => $"D {d.UserId} {d.Day} {d.MetricKey} {d.ContextKey} {d.Value}")
            .Concat(_db.Context.PlayerStatTotals.AsNoTracking().AsEnumerable().Select(t => $"T {t.UserId} {t.MetricKey} {t.ContextKey} {t.Value} {t.ReachedAt:O}"))
            .Concat(_db.Context.PlayerTitleChanges.AsNoTracking().AsEnumerable()
                .Select(c => $"C {c.UserId} {c.CurrencyEntryId} {c.FromTitleBracketId} {c.ToTitleBracketId} {c.Direction} {c.ChangedAt:O}"))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
}
