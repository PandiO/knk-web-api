using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// Currency ledger guarantees that only a real MySQL can prove (currency-payments
/// IMPLEMENTATION_PLAN.md Phase 1 "requires-mysql"): row locks serialize concurrent postings,
/// the idempotency lookup and unique index stop double posts, lock ordering avoids deadlocks,
/// ambient transactions enlist, the triggers refuse UPDATE/DELETE, and retention never purges
/// the ledger. Every test uses its own users, one migrated database per class.
/// </summary>
[Trait("Category", "requires-mysql")]
public class CurrencyLedgerMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public CurrencyLedgerMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static CurrencyService Service(KnKDbContext ctx) =>
        new(new CurrencyRepository(ctx), new UserRepository(ctx), NullLogger<CurrencyService>.Instance);

    private async Task<int> SeedUserAsync(int coins = 0, int gems = 0)
    {
        await using var ctx = _db.NewContext();
        var user = new User { Username = "u" + Guid.NewGuid().ToString("N")[..12], Coins = coins, Gems = gems };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private async Task<User> ReloadAsync(int id)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    private async Task<int> LedgerRowsForAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.CurrencyEntries.CountAsync(e => e.UserId == userId);
    }

    private static CurrencyContext Sys(string reason, string key) => CurrencyContext.ForSystem("MySqlTest", reason, key);

    /// <summary>Runs <paramref name="count"/> calls at once, each on its own DbContext (= its own connection/request).</summary>
    private async Task<List<(PostingResult? Result, Exception? Error)>> InParallelAsync(int count, Func<ICurrencyService, int, Task<PostingResult>> call)
    {
        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            await start.Task;
            await using var ctx = _db.NewContext();
            try
            {
                return ((PostingResult?)await call(Service(ctx), i), (Exception?)null);
            }
            catch (Exception ex)
            {
                return ((PostingResult?)null, ex);
            }
        }).ToList();
        start.SetResult();
        return (await Task.WhenAll(tasks)).ToList();
    }

    [MySqlFact]
    public async Task FiftyParallelSpends_OfTen_AgainstOneHundred_ExactlyTenSucceed()
    {
        var id = await SeedUserAsync(coins: 100);

        var outcomes = await InParallelAsync(50, (s, i) => s.SpendAsync(id, Currency.Coins, 10, Sys(CurrencyReasons.TeleportFee, $"tp:{id}:{i}")));

        Assert.Equal(10, outcomes.Count(o => o.Result != null));
        Assert.All(outcomes.Where(o => o.Error != null), o =>
            Assert.Equal(CurrencyErrorCode.InsufficientFunds, Assert.IsType<CurrencyException>(o.Error).Code));
        Assert.Equal(0, (await ReloadAsync(id)).Coins);
        Assert.Equal(10, await LedgerRowsForAsync(id));
        // Every success saw the balance the previous one left: 100, 90, … 10.
        Assert.Equal(Enumerable.Range(1, 10).Select(n => n * 10L).OrderBy(x => x),
            outcomes.Where(o => o.Result != null).Select(o => o.Result!.Entries.Single().BalanceBefore).OrderBy(x => x));
        await using var check = _db.NewContext();
        Assert.Empty(await new CurrencyReconciler(check).FindMismatchesAsync(id));
    }

    [MySqlFact]
    public async Task TwentyParallelIdenticalGrants_PostOnce()
    {
        var id = await SeedUserAsync(coins: 0);

        var outcomes = await InParallelAsync(20, (s, _) => s.GrantAsync(id, Currency.Coins, 500, Sys(CurrencyReasons.TitleBonus, $"title-bonus:{id}:1")));

        Assert.All(outcomes, o => Assert.Null(o.Error));
        Assert.Single(outcomes, o => !o.Result!.Replayed);
        Assert.Single(outcomes.Select(o => o.Result!.TransactionId).Distinct());
        Assert.Equal(500, (await ReloadAsync(id)).Coins);
        Assert.Equal(1, await LedgerRowsForAsync(id));
    }

    [MySqlFact]
    public async Task SameKeyRacingOnDifferentRows_TheUniqueIndexLetsOnlyOneThrough()
    {
        // Different users → different row locks, so only the unique index can stop the second post.
        var users = new[] { await SeedUserAsync(), await SeedUserAsync(), await SeedUserAsync(), await SeedUserAsync() };
        var key = "race-" + Guid.NewGuid().ToString("N");

        var outcomes = await InParallelAsync(12, (s, i) => s.GrantAsync(users[i % users.Length], Currency.Gems, 5, Sys(CurrencyReasons.EventReward, key)));

        Assert.Single(outcomes, o => o.Result is { Replayed: false });
        Assert.All(outcomes.Where(o => o.Error != null), o =>
            Assert.Equal(CurrencyErrorCode.IdempotencyKeyReuse, Assert.IsType<CurrencyException>(o.Error).Code));
        var paid = 0;
        foreach (var u in users) paid += (await ReloadAsync(u)).Gems;
        Assert.Equal(5, paid);
    }

    [MySqlFact]
    public async Task OppositeLockOrders_DoNotDeadlock()
    {
        var a = await SeedUserAsync(coins: 1_000);
        var b = await SeedUserAsync(coins: 1_000);

        var outcomes = await InParallelAsync(30, (s, i) => s.PostAsync(i % 2 == 0
                ? new[] { new CurrencyLeg(a, Currency.Coins, -1), new CurrencyLeg(b, Currency.Coins, -1) }
                : new[] { new CurrencyLeg(b, Currency.Coins, -1), new CurrencyLeg(a, Currency.Coins, -1) },
            Sys(CurrencyReasons.TeleportFee, $"pair:{a}:{i}")));

        Assert.All(outcomes, o => Assert.Null(o.Error));
        Assert.Equal(970, (await ReloadAsync(a)).Coins);
        Assert.Equal(970, (await ReloadAsync(b)).Coins);
    }

    [MySqlFact]
    public async Task PresenceUpdatesDuringPostings_LoseNothing()
    {
        // Phase 0's deferred check: an unrelated user write racing a balance change.
        var id = await SeedUserAsync(coins: 0);
        var start = new TaskCompletionSource();
        var grants = Enumerable.Range(0, 20).Select(async i =>
        {
            await start.Task;
            await using var ctx = _db.NewContext();
            await Service(ctx).GrantAsync(id, Currency.Coins, 1, Sys(CurrencyReasons.EventReward, $"presence:{id}:{i}"));
        });
        var presence = Enumerable.Range(0, 20).Select(async i =>
        {
            await start.Task;
            await using var ctx = _db.NewContext();
            var repo = new UserRepository(ctx);
            var copy = await repo.GetByIdAsync(id);
            await repo.UpdatePresenceAsync(id, i % 2 == 0);
            copy!.FrozenReason = "touch " + i;
            await repo.UpdateUserAsync(copy);
        });
        var all = grants.Concat(presence).ToList();
        start.SetResult();
        await Task.WhenAll(all);

        Assert.Equal(20, (await ReloadAsync(id)).Coins);
        await using var check = _db.NewContext();
        Assert.Empty(await new CurrencyReconciler(check).FindMismatchesAsync(id));
    }

    [MySqlFact]
    public async Task AmbientTransaction_IsEnlisted_NotCommitted()
    {
        var id = await SeedUserAsync(coins: 50);

        await using (var ctx = _db.NewContext())
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();
            await Service(ctx).SpendAsync(id, Currency.Coins, 20, Sys(CurrencyReasons.LootboxPurchase, $"lootbox-open:{id}:1"));
            await tx.RollbackAsync(); // e.g. the caller's own domain row failed
        }
        Assert.Equal(50, (await ReloadAsync(id)).Coins);
        Assert.Equal(0, await LedgerRowsForAsync(id));

        await using (var ctx = _db.NewContext())
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();
            await Service(ctx).SpendAsync(id, Currency.Coins, 20, Sys(CurrencyReasons.LootboxPurchase, $"lootbox-open:{id}:1"));
            await tx.CommitAsync();
        }
        Assert.Equal(30, (await ReloadAsync(id)).Coins);
        Assert.Equal(1, await LedgerRowsForAsync(id));
    }

    [MySqlFact]
    public async Task IdempotencyKeys_AreCaseSensitive()
    {
        var id = await SeedUserAsync();
        await using var ctx = _db.NewContext();
        var service = Service(ctx);
        var key = "Case-" + Guid.NewGuid().ToString("N");

        await service.GrantAsync(id, Currency.Coins, 1, Sys(CurrencyReasons.EventReward, key));
        var other = await service.GrantAsync(id, Currency.Coins, 2, Sys(CurrencyReasons.EventReward, key.ToLowerInvariant()));

        Assert.False(other.Replayed);
        Assert.Equal(3, (await ReloadAsync(id)).Coins);
    }

    [MySqlFact]
    public async Task Triggers_RefuseUpdateAndDelete()
    {
        if (_db.TriggersSkipped)
        {
            return;
        }
        var id = await SeedUserAsync();
        await using var ctx = _db.NewContext();
        var posted = await Service(ctx).GrantAsync(id, Currency.Coins, 10, Sys(CurrencyReasons.EventReward, $"trigger:{id}"));

        foreach (var sql in new[]
        {
            $"UPDATE currency_entries SET Amount = 1000 WHERE TransactionId = {posted.TransactionId}",
            $"DELETE FROM currency_entries WHERE TransactionId = {posted.TransactionId}",
            $"UPDATE currency_transactions SET Reason = 'edited' WHERE Id = {posted.TransactionId}",
            $"DELETE FROM currency_transactions WHERE Id = {posted.TransactionId}"
        })
        {
            var ex = await Assert.ThrowsAsync<MySqlException>(() => ctx.Database.ExecuteSqlRawAsync(sql));
            Assert.Equal("45000", ex.SqlState);
            Assert.Contains("append-only", ex.Message);
        }
        Assert.Equal(2, await ctx.CurrencyEntries.CountAsync(e => e.TransactionId == posted.TransactionId));
    }

    [MySqlFact]
    public async Task CheckConstraints_RejectABadLeg()
    {
        var id = await SeedUserAsync();
        await using var ctx = _db.NewContext();
        var posted = await Service(ctx).GrantAsync(id, Currency.Coins, 1, Sys(CurrencyReasons.EventReward, $"ck:{id}"));
        // before + amount ≠ after
        var ex = await Assert.ThrowsAsync<MySqlException>(() => ctx.Database.ExecuteSqlRawAsync(
            $"INSERT INTO currency_entries (TransactionId, Currency, AccountKind, UserId, Operation, Amount, BalanceBefore, BalanceAfter) VALUES ({posted.TransactionId}, 0, 0, {id}, 0, 5, 0, 6)"));
        Assert.Equal(3819, ex.Number); // ER_CHECK_CONSTRAINT_VIOLATED
    }

    [MySqlFact]
    public async Task RetentionPolicyService_NeverPurgesTheLedger()
    {
        var id = await SeedUserAsync();
        long txId;
        await using (var ctx = _db.NewContext())
        {
            txId = (await Service(ctx).GrantAsync(id, Currency.Coins, 1, Sys(CurrencyReasons.EventReward, $"old:{id}"))).TransactionId;
            ctx.AuditLogEntries.Add(new AuditLogEntry { TargetUserId = id, Action = AuditAction.BalanceAdjusted, Timestamp = DateTime.UtcNow.AddYears(-5) });
            await ctx.SaveChangesAsync();
        }
        if (_db.TriggersSkipped)
        {
            // Without triggers, age the row so a date-based purge would have matched it.
            await using var ctx = _db.NewContext();
            await ctx.Database.ExecuteSqlRawAsync($"UPDATE currency_transactions SET CreatedAt = '2015-01-01' WHERE Id = {txId}");
        }

        var services = new ServiceCollection();
        services.AddDbContext<KnKDbContext>(o => o.UseMySql(_db.ConnectionString, ServerVersion.AutoDetect(_db.ConnectionString)));
        services.AddScoped<IFormSubmissionProgressRepository, FormSubmissionProgressRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IAuditLogRetentionConfigurationRepository, AuditLogRetentionConfigurationRepository>();
        services.AddScoped<IAuditLogRetentionConfigurationService, AuditLogRetentionConfigurationService>();
        await using var provider = services.BuildServiceProvider();
        var retention = new RetentionPolicyService(provider, NullLogger<RetentionPolicyService>.Instance);

        await retention.StartAsync(CancellationToken.None);
        await using (var ctx = _db.NewContext())
        {
            // The first cleanup runs on start; wait until it has deleted the old audit entry.
            for (var i = 0; i < 100 && await ctx.AuditLogEntries.AnyAsync(e => e.TargetUserId == id); i++)
            {
                await Task.Delay(100);
            }
            Assert.False(await ctx.AuditLogEntries.AnyAsync(e => e.TargetUserId == id));
        }
        await retention.StopAsync(CancellationToken.None);

        await using var check = _db.NewContext();
        Assert.True(await check.CurrencyTransactions.AnyAsync(t => t.Id == txId));
        Assert.Equal(1, await LedgerRowsForAsync(id));
    }
}
