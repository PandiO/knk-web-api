using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using static knkwebapi_v2.Tests.Services.CurrencyLedgerSeed;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Arena duels in the currency monitor (DESIGN.md §10.3, KNG-213): R3 and R4 read a duel payout as a flow
/// from the loser to the winner, R7 leaves the stake-funded DUEL_PAYOUT out, and R10 catches duel payouts
/// flowing one way between a pair.
/// </summary>
public class CurrencyAnomalyDetectorDuelTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _db = $"currency-monitor-duels-{Guid.NewGuid()}";

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_db).Options);

    private async Task<List<CurrencyAlertDraft>> RunAsync(Func<CurrencyAnomalyDetector, Task<List<CurrencyAlertDraft>>> rule)
    {
        await using var ctx = NewContext();
        return await rule(new CurrencyAnomalyDetector(ctx, Options.Create(new CurrencyMonitorOptions())));
    }

    private async Task SeedUsersAsync(DateTime createdAt, params int[] ids)
    {
        await using var ctx = NewContext();
        foreach (var id in ids) ctx.Users.Add(new User { Id = id, Username = $"player{id}", CreatedAt = createdAt });
        await ctx.SaveChangesAsync();
    }

    /// <summary>A paid duel: the payout posting and the match row pointing at it.</summary>
    private async Task SeedDuelAsync(int winner, int loser, long stake, DateTime at)
    {
        await using var ctx = NewContext();
        var payout = Tx(CurrencyTransactionKind.Grant, CurrencyReasons.DuelPayout, at, (winner, Currency.Coins, stake * 2));
        await AddAsync(ctx, payout);
        ctx.DuelMatches.Add(new DuelMatch
        {
            PublicToken = Guid.NewGuid().ToString(), ArenaId = 1, ChallengerUserId = winner, OpponentUserId = loser,
            WinnerUserId = winner, Status = DuelMatchStatus.Completed, ChallengerCoins = stake, OpponentCoins = stake,
            SettlementTransactionId = payout.Id, CreatedAt = at, EndedAt = at
        });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Funnel_CountsDuelsWonFromYoungAccounts()
    {
        await SeedUsersAsync(Now.AddDays(-400), 100);
        await SeedUsersAsync(Now.AddDays(-2), 1, 2, 3, 4, 5);
        foreach (var loser in new[] { 1, 2, 3, 4, 5 }) await SeedDuelAsync(100, loser, 500, Now.AddHours(-loser));

        var draft = Assert.Single(await RunAsync(d => d.FunnelAsync(Now)));

        Assert.Equal((CurrencyAlertRules.Funnel, 100), (draft.Rule, draft.UserId));
    }

    [Fact]
    public async Task PingPong_CountsDuelsWonBackAndForth()
    {
        await SeedUsersAsync(Now.AddDays(-100), 1, 2);
        for (var i = 0; i < 3; i++)
        {
            await SeedDuelAsync(1, 2, 100, Now.AddMinutes(-50 + i * 10));
            await SeedDuelAsync(2, 1, 100, Now.AddMinutes(-45 + i * 10));
        }

        var draft = Assert.Single(await RunAsync(d => d.PingPongAsync(Now)));

        Assert.Equal("R4:pair:1:2", draft.DedupKey);
    }

    [Fact]
    public async Task MintRate_LeavesDuelPayoutsOut()
    {
        await SeedUsersAsync(Now.AddDays(-100), 1, 2);
        for (var day = 1; day <= 7; day++) await SeedDuelAsync(1, 2, 1_000, Now.AddDays(-day).AddHours(-1));
        await SeedDuelAsync(1, 2, 50_000, Now.AddHours(-1));

        Assert.Empty(await RunAsync(d => d.MintRateAsync(Now)));
    }

    [Fact]
    public async Task DuelPair_ThreeOneWayWins_OrABigNetFlow_IsMedium()
    {
        await SeedUsersAsync(Now.AddDays(-100), 1, 2, 3, 4, 5, 6);
        for (var i = 1; i <= 3; i++) await SeedDuelAsync(2, 1, 100, Now.AddHours(-i));      // 2 beats 1 three times
        await SeedDuelAsync(4, 3, 20_000, Now.AddHours(-1));                                   // one big pot
        await SeedDuelAsync(5, 6, 100, Now.AddHours(-1));                                      // 5 and 6 trade wins
        await SeedDuelAsync(6, 5, 100, Now.AddHours(-2));
        await SeedDuelAsync(5, 6, 100, Now.AddHours(-3));
        await SeedDuelAsync(2, 1, 100, Now.AddHours(-30));                                     // outside the window

        var drafts = (await RunAsync(d => d.DuelPairAsync(Now))).OrderBy(d => d.DedupKey).ToList();

        Assert.Equal(new[] { "R10:pair:1:2", "R10:pair:3:4" }, drafts.Select(d => d.DedupKey));
        Assert.All(drafts, d => Assert.Equal((CurrencyAlertRules.DuelPair, CurrencyAlertSeverity.Medium), (d.Rule, d.Severity)));
        Assert.Equal((2, 4), (drafts[0].UserId, drafts[1].UserId));
        Assert.Contains("won 3 paid duels against player1", drafts[0].Summary);
    }
}
