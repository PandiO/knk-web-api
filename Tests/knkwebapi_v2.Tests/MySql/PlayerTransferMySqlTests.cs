using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// Player-transfer guarantees only a real MySQL can prove (currency-payments
/// IMPLEMENTATION_PLAN.md Phase 3 "requires-mysql"): concurrent sends from one sender can't
/// overdraw, a replayed key pays once, the daily cap holds across concurrent requests, A→B racing
/// B→A doesn't deadlock, and concurrent confirmations of one pending transfer pay once. Uses the
/// migration-seeded policies and title brackets, tuned per test with SQL.
/// </summary>
[Trait("Category", "requires-mysql")]
public class PlayerTransferMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public PlayerTransferMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static CurrencyService Service(KnKDbContext ctx) =>
        new(new CurrencyRepository(ctx), new UserRepository(ctx), NullLogger<CurrencyService>.Instance);

    private async Task<int> SeedUserAsync(int coins = 0)
    {
        await using var ctx = _db.NewContext();
        var user = new User { Username = "t" + Guid.NewGuid().ToString("N")[..12], CreatedAt = DateTime.UtcNow.AddDays(-10) };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        // Balances and XP aren't written by EF: old enough, Peasant (2,500 XP), starting coins.
        await ctx.Database.ExecuteSqlInterpolatedAsync($"UPDATE users SET Coins = {coins}, ExperiencePoints = 5000 WHERE Id = {user.Id}");
        return user.Id;
    }

    /// <summary>Coin policy for the test: no cooldown/hourly limit unless given.</summary>
    private async Task SetCoinPolicyAsync(long dailySendCap = 2_000_000, long confirmThreshold = 100_000)
    {
        await using var ctx = _db.NewContext();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE currency_policies SET CooldownSeconds = 0, MaxTransfersPerHour = 10000, DailySendCap = {dailySendCap}, DailyReceiveCap = 100000000, ConfirmThreshold = {confirmThreshold}, MinTransfer = 1, TransfersEnabled = 1, Transferable = 1 WHERE Currency = 0");
    }

    private async Task<User> ReloadAsync(int id)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    private static CurrencyContext Pay(int sender, string key) => new()
    {
        IdempotencyKey = key,
        IdempotencyScope = CurrencyIdempotencyScopes.Plugin,
        ReasonCode = CurrencyReasons.PlayerTransfer,
        Initiator = CurrencyInitiator.Player,
        InitiatorUserId = sender,
        InitiatorComponent = "MySqlTest"
    };

    private async Task<List<(T? Result, Exception? Error)>> InParallelAsync<T>(int count, Func<CurrencyService, int, Task<T>> call) where T : class
    {
        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            await start.Task;
            await using var ctx = _db.NewContext();
            try
            {
                return ((T?)await call(Service(ctx), i), (Exception?)null);
            }
            catch (Exception ex)
            {
                return ((T?)null, ex);
            }
        }).ToList();
        start.SetResult();
        return (await Task.WhenAll(tasks)).ToList();
    }

    private async Task AssertReconciledAsync(params int[] ids)
    {
        await using var check = _db.NewContext();
        foreach (var id in ids)
        {
            Assert.Empty(await new CurrencyReconciler(check).FindMismatchesAsync(id));
        }
    }

    [MySqlFact]
    public async Task ReversedTransfer_FreesTheSendReceiveAndHourlyAllowance()
    {
        // KNG-21 developer decision: a reversed PLAYER_TRANSFER stops counting toward the
        // sender's daily send cap and hourly count and the recipient's daily receive cap.
        await using (var ctx = _db.NewContext())
        {
            await ctx.Database.ExecuteSqlRawAsync(
                "UPDATE currency_policies SET CooldownSeconds = 0, MaxTransfersPerHour = 1, DailySendCap = 1000, DailyReceiveCap = 1000, ConfirmThreshold = 100000, MinTransfer = 1, TransferFeeBasisPoints = 0, TransfersEnabled = 1, Transferable = 1 WHERE Currency = 0");
        }
        var sender = await SeedUserAsync(coins: 5000);
        var other = await SeedUserAsync(coins: 5000);
        var third = await SeedUserAsync(coins: 5000);
        var recipient = await SeedUserAsync();

        long sentId;
        await using (var ctx = _db.NewContext())
        {
            sentId = (await Service(ctx).TransferAsync(new TransferRequest(sender, recipient, Currency.Coins, 1000),
                Pay(sender, Guid.NewGuid().ToString("N")))).TransactionId!.Value;
        }
        async Task<CurrencyErrorCode?> TryPay(int from, long amount)
        {
            await using var ctx = _db.NewContext();
            try
            {
                await Service(ctx).TransferAsync(new TransferRequest(from, recipient, Currency.Coins, amount), Pay(from, Guid.NewGuid().ToString("N")));
                return null;
            }
            catch (CurrencyException ex)
            {
                return ex.Code;
            }
        }
        Assert.Equal(CurrencyErrorCode.CooldownActive, await TryPay(sender, 10));
        Assert.Equal(CurrencyErrorCode.RecipientDailyCapExceeded, await TryPay(other, 10));

        await using (var ctx = _db.NewContext())
        {
            await Service(ctx).ReverseAsync(sentId, new ReversalOptions(), new CurrencyContext
            {
                IdempotencyKey = $"reverse:{sentId}",
                IdempotencyScope = CurrencyIdempotencyScopes.Web,
                ReasonCode = CurrencyReasons.Reversal,
                Reason = "Scam reported by the sender",
                Initiator = CurrencyInitiator.Admin,
                InitiatorUserId = other,
                InitiatorComponent = "MySqlTest"
            });
        }

        Assert.Null(await TryPay(other, 400));
        Assert.Null(await TryPay(sender, 600));
        Assert.Equal(CurrencyErrorCode.RecipientDailyCapExceeded, await TryPay(third, 1));
        Assert.Equal((4400, 1000), ((await ReloadAsync(sender)).Coins, (await ReloadAsync(recipient)).Coins));
        await AssertReconciledAsync(sender, other, third, recipient);
    }

    [MySqlFact]
    public async Task ConcurrentSendsFromOneSender_CantOverdraw()
    {
        await SetCoinPolicyAsync();
        var sender = await SeedUserAsync(coins: 1000);
        var recipients = new[] { await SeedUserAsync(), await SeedUserAsync(), await SeedUserAsync() };

        var outcomes = await InParallelAsync(25, (s, i) =>
            s.TransferAsync(new TransferRequest(sender, recipients[i % 3], Currency.Coins, 100), Pay(sender, $"od:{sender}:{i}")));

        Assert.Equal(10, outcomes.Count(o => o.Result != null));
        Assert.All(outcomes.Where(o => o.Error != null), o =>
            Assert.Equal(CurrencyErrorCode.InsufficientFunds, Assert.IsType<CurrencyException>(o.Error).Code));
        Assert.Equal(0, (await ReloadAsync(sender)).Coins);
        var received = 0;
        foreach (var r in recipients) received += (await ReloadAsync(r)).Coins;
        Assert.Equal(1000, received);
        await AssertReconciledAsync(recipients.Append(sender).ToArray());
    }

    [MySqlFact]
    public async Task ReplayedKey_PaysOnce_EvenWhenTheRetriesRace()
    {
        await SetCoinPolicyAsync();
        var sender = await SeedUserAsync(coins: 5000);
        var recipient = await SeedUserAsync();
        var key = "retry-" + Guid.NewGuid().ToString("N");

        var outcomes = await InParallelAsync(15, (s, _) =>
            s.TransferAsync(new TransferRequest(sender, recipient, Currency.Coins, 700), Pay(sender, key)));

        Assert.All(outcomes, o => Assert.Null(o.Error));
        Assert.Single(outcomes, o => !o.Result!.Replayed);
        Assert.Single(outcomes.Select(o => o.Result!.PublicId).Distinct());
        Assert.Equal((4300, 700), ((await ReloadAsync(sender)).Coins, (await ReloadAsync(recipient)).Coins));
        await AssertReconciledAsync(sender, recipient);
    }

    [MySqlFact]
    public async Task DailyCap_HoldsExactly_AcrossConcurrentSends()
    {
        await SetCoinPolicyAsync(dailySendCap: 1000);
        var sender = await SeedUserAsync(coins: 10_000);
        var recipient = await SeedUserAsync();

        var outcomes = await InParallelAsync(10, (s, i) =>
            s.TransferAsync(new TransferRequest(sender, recipient, Currency.Coins, 150), Pay(sender, $"cap:{sender}:{i}")));

        Assert.Equal(6, outcomes.Count(o => o.Result != null));
        Assert.All(outcomes.Where(o => o.Error != null), o =>
            Assert.Equal(CurrencyErrorCode.DailyCapExceeded, Assert.IsType<CurrencyException>(o.Error).Code));
        Assert.Equal(900, (await ReloadAsync(recipient)).Coins);
        Assert.Equal(9100, (await ReloadAsync(sender)).Coins);
    }

    [MySqlFact]
    public async Task OppositeTransfers_DoNotDeadlock()
    {
        await SetCoinPolicyAsync();
        var a = await SeedUserAsync(coins: 1000);
        var b = await SeedUserAsync(coins: 1000);

        var outcomes = await InParallelAsync(30, (s, i) => i % 2 == 0
            ? s.TransferAsync(new TransferRequest(a, b, Currency.Coins, 10), Pay(a, $"ab:{a}:{i}"))
            : s.TransferAsync(new TransferRequest(b, a, Currency.Coins, 3), Pay(b, $"ba:{b}:{i}")));

        Assert.All(outcomes, o => Assert.Null(o.Error));
        Assert.Equal((1000 - 150 + 45, 1000 + 150 - 45), ((await ReloadAsync(a)).Coins, (await ReloadAsync(b)).Coins));
        await AssertReconciledAsync(a, b);
    }

    [MySqlFact]
    public async Task ConcurrentConfirmations_PayOnce()
    {
        await SetCoinPolicyAsync(confirmThreshold: 500);
        var sender = await SeedUserAsync(coins: 5000);
        var recipient = await SeedUserAsync();
        TransferResultDto created;
        await using (var ctx = _db.NewContext())
        {
            created = await Service(ctx).TransferAsync(new TransferRequest(sender, recipient, Currency.Coins, 800), Pay(sender, $"big:{sender}"));
        }
        Assert.Equal(TransferResultDto.StatusPendingConfirmation, created.Status);

        var outcomes = await InParallelAsync(10, (s, _) => s.ConfirmTransferAsync(created.Pending!.PublicId, sender, Pay(sender, "confirm")));

        Assert.All(outcomes, o => Assert.Null(o.Error));
        Assert.Single(outcomes, o => !o.Result!.Replayed);
        Assert.Equal((4200, 800), ((await ReloadAsync(sender)).Coins, (await ReloadAsync(recipient)).Coins));
        await using var check = _db.NewContext();
        var pending = await check.CurrencyPendingTransfers.AsNoTracking().SingleAsync(p => p.PublicId == created.Pending!.PublicId);
        Assert.Equal(CurrencyPendingTransferStatus.Confirmed, pending.Status);
        Assert.Equal(outcomes[0].Result!.TransactionId, pending.ResultTransactionId);
    }

    [MySqlFact]
    public async Task CancelRacingConfirm_NeverBoth()
    {
        await SetCoinPolicyAsync(confirmThreshold: 500);
        var sender = await SeedUserAsync(coins: 5000);
        var recipient = await SeedUserAsync();
        TransferResultDto created;
        await using (var ctx = _db.NewContext())
        {
            created = await Service(ctx).TransferAsync(new TransferRequest(sender, recipient, Currency.Coins, 800), Pay(sender, $"race:{sender}"));
        }
        var id = created.Pending!.PublicId;

        var outcomes = await InParallelAsync<object>(10, async (s, i) => i % 2 == 0
            ? (object)await s.ConfirmTransferAsync(id, sender, Pay(sender, "c"))
            : await s.CancelTransferAsync(id, sender));

        var coins = (await ReloadAsync(recipient)).Coins;
        await using var check = _db.NewContext();
        var status = (await check.CurrencyPendingTransfers.AsNoTracking().SingleAsync(p => p.PublicId == id)).Status;
        if (status == CurrencyPendingTransferStatus.Confirmed)
        {
            Assert.Equal(800, coins);
            Assert.All(outcomes.Where(o => o.Error != null), o =>
                Assert.Equal(CurrencyErrorCode.PendingTransferClosed, Assert.IsType<CurrencyException>(o.Error).Code));
        }
        else
        {
            Assert.Equal(CurrencyPendingTransferStatus.Cancelled, status);
            Assert.Equal(0, coins);
        }
        await AssertReconciledAsync(sender, recipient);
    }
}
