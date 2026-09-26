using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Currency ledger core (currency-payments IMPLEMENTATION_PLAN.md Phase 1): double entry, reason
/// mapping, bounds, idempotency, admin set, reversals, reconciliation, history.
/// <para>
/// These run on EF InMemory, where UserRepository.RunWithUsersLockedAsync is a no-op wrapper (no
/// transaction, no FOR UPDATE) and unique indexes aren't enforced. Locking, the unique-index
/// race and the immutability triggers are proven against a real MySQL in
/// Tests/MySql/CurrencyLedgerMySqlTests.cs (requires-mysql).
/// </para>
/// </summary>
public class CurrencyServiceTests
{
    private readonly string _db = $"currency-{Guid.NewGuid()}";

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_db).Options);

    private static CurrencyService Service(KnKDbContext ctx) =>
        new(new CurrencyRepository(ctx), new UserRepository(ctx), NullLogger<CurrencyService>.Instance);

    private async Task<int> SeedUserAsync(string name = "player", int coins = 100, int gems = 10, int xp = 0)
    {
        await using var ctx = NewContext();
        var user = new User { Username = name, Coins = coins, Gems = gems, ExperiencePoints = xp };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private static CurrencyContext System(string reason, string key) =>
        CurrencyContext.ForSystem("TestComponent", reason, key);

    private static CurrencyContext Staff(string reason, string key, string note = "Compensation for a bug") => new()
    {
        IdempotencyKey = key,
        IdempotencyScope = CurrencyIdempotencyScopes.Web,
        ReasonCode = reason,
        Reason = note,
        Initiator = CurrencyInitiator.Admin,
        InitiatorUserId = 999,
        InitiatorComponent = "WebAppProfile"
    };

    private async Task<User> ReloadAsync(int id)
    {
        await using var ctx = NewContext();
        return await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    private async Task<List<CurrencyTransaction>> TransactionsAsync()
    {
        await using var ctx = NewContext();
        return await ctx.CurrencyTransactions.AsNoTracking().Include(t => t.Entries).OrderBy(t => t.Id).ToListAsync();
    }

    // ===== Grants, spends, double entry =====

    [Fact]
    public async Task Grant_WritesBalancedEntries_AndUpdatesTheBalance()
    {
        var id = await SeedUserAsync(coins: 100);
        await using var ctx = NewContext();

        var result = await Service(ctx).GrantAsync(id, Currency.Coins, 250, CurrencyContext.ForSystem("SalaryService", CurrencyReasons.Salary, $"salary:{id}:t0"));

        Assert.False(result.Replayed);
        Assert.Equal(26, result.PublicId.Length);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(("Coins", "Add", 250L, 100L, 350L), (entry.Currency, entry.Operation, entry.Amount, entry.BalanceBefore, entry.BalanceAfter));
        Assert.Equal(350, result.Balances[id].Coins);
        Assert.Equal(350, (await ReloadAsync(id)).Coins);

        var tx = Assert.Single(await TransactionsAsync());
        Assert.Equal(CurrencyTransactionKind.Grant, tx.Kind);
        Assert.Equal("SALARY", tx.ReasonCode);
        Assert.Equal("Salary payout", tx.Reason);
        Assert.Equal(CurrencyInitiator.System, tx.Initiator);
        Assert.Equal("SalaryService", tx.InitiatorComponent);
        Assert.Equal(64, tx.RequestHash.Length);
        Assert.Equal(0, tx.Entries.Sum(e => e.Amount));
        var system = Assert.Single(tx.Entries, e => e.AccountKind == CurrencyAccountKind.System);
        Assert.Equal(("SYS_SALARY", -250L, (long?)null), (system.SystemAccount!, system.Amount, system.BalanceAfter));
    }

    [Fact]
    public async Task Spend_Insufficient_IsRefused_AndWritesNothing()
    {
        var id = await SeedUserAsync(gems: 5);
        await using var ctx = NewContext();

        var ex = await Assert.ThrowsAsync<CurrencyException>(() =>
            Service(ctx).SpendAsync(id, Currency.Gems, 6, System(CurrencyReasons.KitPurchase, "kit-purchase:1:1")));

        Assert.Equal(CurrencyErrorCode.InsufficientFunds, ex.Code);
        Assert.StartsWith("InsufficientFunds", ex.Message);
        Assert.Equal(5, (await ReloadAsync(id)).Gems);
        Assert.Empty(await TransactionsAsync());

        // A refusal isn't stored: the same key works once the player can afford it.
        await Service(ctx).SpendAsync(id, Currency.Gems, 5, System(CurrencyReasons.KitPurchase, "kit-purchase:1:1"));
        Assert.Equal(0, (await ReloadAsync(id)).Gems);
    }

    [Fact]
    public async Task Grant_AboveTheCap_IsRefused()
    {
        var id = await SeedUserAsync(coins: BalanceLimits.MaxCoins - 5);
        await using var ctx = NewContext();

        var ex = await Assert.ThrowsAsync<CurrencyException>(() =>
            Service(ctx).GrantAsync(id, Currency.Coins, 6, System(CurrencyReasons.EventReward, "event:1:1")));
        Assert.Equal(CurrencyErrorCode.BalanceCapExceeded, ex.Code);
        Assert.Equal(BalanceLimits.MaxCoins - 5, (await ReloadAsync(id)).Coins);

        await Service(ctx).GrantAsync(id, Currency.Coins, 5, System(CurrencyReasons.EventReward, "event:1:2"));
        Assert.Equal(BalanceLimits.MaxCoins, (await ReloadAsync(id)).Coins);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(1_000_000L)]
    [InlineData(long.MaxValue)]
    public async Task AmountOutsideOneToCap_IsRefused(long amount)
    {
        var id = await SeedUserAsync();
        await using var ctx = NewContext();

        var ex = await Assert.ThrowsAsync<CurrencyException>(() =>
            Service(ctx).GrantAsync(id, Currency.Gems, amount, System(CurrencyReasons.EventReward, "event:x")));
        Assert.Equal(CurrencyErrorCode.AmountOutOfRange, ex.Code);
    }

    [Fact]
    public async Task Experience_IsInTheSameLedger()
    {
        var id = await SeedUserAsync(xp: 40);
        await using var ctx = NewContext();

        var result = await Service(ctx).GrantAsync(id, Currency.Experience, 2_000, System(CurrencyReasons.DiscoveryReward, $"discovery:{id}:7"));

        Assert.Equal(2_040, (await ReloadAsync(id)).ExperiencePoints);
        Assert.Equal("Experience", result.Entries.Single().Currency);
        Assert.Equal(2_040, result.Balances[id].ExperiencePoints);
    }

    [Fact]
    public async Task MultiLegPost_OneSystemLegPerCurrency_AllOrNothing()
    {
        var a = await SeedUserAsync("a", coins: 0);
        var b = await SeedUserAsync("b", coins: 0);
        await using var ctx = NewContext();
        var service = Service(ctx);

        await service.PostAsync(new[]
        {
            new CurrencyLeg(a, Currency.Coins, 100),
            new CurrencyLeg(b, Currency.Coins, 50),
            new CurrencyLeg(a, Currency.Experience, 10)
        }, System(CurrencyReasons.SiegeReward, "siege-match:1"));

        var tx = Assert.Single(await TransactionsAsync());
        Assert.Equal(5, tx.Entries.Count);
        Assert.Equal(-150, tx.Entries.Single(e => e.AccountKind == CurrencyAccountKind.System && e.Currency == Currency.Coins).Amount);
        Assert.Equal(-10, tx.Entries.Single(e => e.AccountKind == CurrencyAccountKind.System && e.Currency == Currency.Experience).Amount);

        // One leg over the cap: nobody is paid.
        var ex = await Assert.ThrowsAsync<CurrencyException>(() => service.PostAsync(new[]
        {
            new CurrencyLeg(a, Currency.Coins, 5),
            new CurrencyLeg(b, Currency.Coins, BalanceLimits.MaxCoins)
        }, System(CurrencyReasons.SiegeReward, "siege-match:2")));
        Assert.Equal(CurrencyErrorCode.BalanceCapExceeded, ex.Code);
        Assert.Equal(100, (await ReloadAsync(a)).Coins);
        Assert.Equal(50, (await ReloadAsync(b)).Coins);
        Assert.Single(await TransactionsAsync());

        var dup = await Assert.ThrowsAsync<CurrencyException>(() => service.PostAsync(new[]
        {
            new CurrencyLeg(a, Currency.Coins, 5), new CurrencyLeg(a, Currency.Coins, 5)
        }, System(CurrencyReasons.SiegeReward, "siege-match:3")));
        Assert.Equal(CurrencyErrorCode.InvalidRequest, dup.Code);
    }

    [Fact]
    public async Task UnknownUser_IsRefused()
    {
        await using var ctx = NewContext();
        var ex = await Assert.ThrowsAsync<CurrencyException>(() =>
            Service(ctx).GrantAsync(4242, Currency.Coins, 1, System(CurrencyReasons.EventReward, "event:1:4242")));
        Assert.Equal(CurrencyErrorCode.UserNotFound, ex.Code);
    }

    // ===== Reason mapping =====

    [Fact]
    public void ReasonTable_EveryCreditOrDebitReasonHasASystemAccount()
    {
        foreach (var reason in CurrencyReasons.All)
        {
            Assert.True(reason.Code.Length <= 40, reason.Code);
            if (reason.Direction != CurrencyReasonDirection.Special)
            {
                Assert.False(string.IsNullOrEmpty(reason.SystemAccount), reason.Code);
                Assert.True(reason.SystemAccount!.Length <= 30, reason.Code);
            }
        }
        Assert.Equal(CurrencyTransactionKind.Grant, CurrencyReasons.Find("TITLE_BONUS")!.Kind);
        Assert.Equal("SYS_KITS", CurrencyReasons.Find("KIT_CLAIM_COST")!.SystemAccount);
        Assert.Equal(CurrencyReasons.AdminSet, CurrencyReasons.ForAdminMode(CurrencyOperation.Set));
        Assert.Null(CurrencyReasons.Find("NOPE"));
    }

    [Fact]
    public async Task MisusedReasons_AreRefused()
    {
        var id = await SeedUserAsync();
        await using var ctx = NewContext();
        var service = Service(ctx);

        async Task<CurrencyErrorCode> Code(Func<Task> call) => (await Assert.ThrowsAsync<CurrencyException>(call)).Code;

        Assert.Equal(CurrencyErrorCode.InvalidRequest, await Code(() => service.GrantAsync(id, Currency.Coins, 1, System(CurrencyReasons.KitPurchase, "k1"))));
        Assert.Equal(CurrencyErrorCode.InvalidRequest, await Code(() => service.SpendAsync(id, Currency.Coins, 1, System(CurrencyReasons.Salary, "k2"))));
        Assert.Equal(CurrencyErrorCode.InvalidRequest, await Code(() => service.GrantAsync(id, Currency.Coins, 1, System(CurrencyReasons.AdminGrant, "k3"))));
        Assert.Equal(CurrencyErrorCode.InvalidRequest, await Code(() => service.PostAsync(new[] { new CurrencyLeg(id, Currency.Coins, 1) }, System(CurrencyReasons.PlayerTransfer, "k4"))));
        Assert.Equal(CurrencyErrorCode.InvalidRequest, await Code(() => service.GrantAsync(id, Currency.Coins, 1, System("MADE_UP", "k5"))));
        Assert.Equal(CurrencyErrorCode.AmountOutOfRange, await Code(() => service.PostAsync(new[] { new CurrencyLeg(id, Currency.Coins, -1) }, System(CurrencyReasons.Salary, "k6"))));
        Assert.Empty(await TransactionsAsync());
    }

    [Fact]
    public async Task ContextValidation()
    {
        var id = await SeedUserAsync();
        await using var ctx = NewContext();
        var service = Service(ctx);
        var ok = System(CurrencyReasons.EventReward, "event:1");

        async Task<CurrencyErrorCode> Code(CurrencyContext c) =>
            (await Assert.ThrowsAsync<CurrencyException>(() => service.GrantAsync(id, Currency.Coins, 1, c))).Code;

        Assert.Equal(CurrencyErrorCode.InvalidRequest, await Code(ok with { IdempotencyKey = "" }));
        Assert.Equal(CurrencyErrorCode.InvalidRequest, await Code(ok with { IdempotencyKey = "has space" }));
        Assert.Equal(CurrencyErrorCode.InvalidRequest, await Code(ok with { IdempotencyKey = new string('a', 101) }));
        Assert.Equal(CurrencyErrorCode.InvalidRequest, await Code(ok with { InitiatorComponent = null }));
        Assert.Equal(CurrencyErrorCode.InvalidRequest, await Code(ok with { Initiator = CurrencyInitiator.Admin, InitiatorUserId = null }));
        Assert.Equal(CurrencyErrorCode.InvalidRequest, await Code(ok with { MetadataJson = "{not json" }));
        Assert.Equal(CurrencyErrorCode.InvalidRequest, await Code(ok with { Reason = new string('r', 501) }));

        await service.GrantAsync(id, Currency.Coins, 1, ok with { MetadataJson = "{\"multiplier\":1.5}", CorrelationId = "corr-1", SourceType = "Event", SourceRef = "1" });
        var tx = Assert.Single(await TransactionsAsync());
        Assert.Equal(("corr-1", "Event", "1"), (tx.CorrelationId, tx.SourceType, tx.SourceRef));
        Assert.Equal(1.5, JsonDocument.Parse(tx.MetadataJson!).RootElement.GetProperty("multiplier").GetDouble());
    }

    [Fact]
    public void ForCaller_UsesTheVerifiedActorAndTheCallerScope()
    {
        var staffInGame = CurrencyContext.ForCaller(new KnkCaller(true, false, null, 7), CurrencyReasons.AdminGrant, "k", "PluginStaffCommand", staffAction: true, "note");
        Assert.Equal((CurrencyInitiator.Admin, (int?)7, "plugin"), (staffInGame.Initiator, staffInGame.InitiatorUserId, staffInGame.IdempotencyScope));

        var plugin = CurrencyContext.ForCaller(new KnkCaller(true, false, null, null), CurrencyReasons.KitClaimCost, "k", "KitClaim", staffAction: false);
        Assert.Equal((CurrencyInitiator.PluginService, (int?)null), (plugin.Initiator, plugin.InitiatorUserId));

        var webSelf = CurrencyContext.ForCaller(new KnkCaller(false, true, 12, 99), CurrencyReasons.TeleportFee, "k", "WebApp", staffAction: false);
        Assert.Equal((CurrencyInitiator.Player, (int?)12, "web"), (webSelf.Initiator, webSelf.InitiatorUserId, webSelf.IdempotencyScope));

        Assert.Throws<CurrencyException>(() => CurrencyContext.ForCaller(new KnkCaller(false, false, null, 5), CurrencyReasons.AdminGrant, "k", "x", true));
    }

    // ===== Idempotency =====

    [Fact]
    public async Task SameKeySameRequest_ReplaysTheStoredResult()
    {
        var id = await SeedUserAsync(coins: 0);
        await using var ctx = NewContext();
        var service = Service(ctx);
        var key = System(CurrencyReasons.TitleBonus, $"title-bonus:{id}:3");

        var first = await service.GrantAsync(id, Currency.Coins, 500, key);
        await service.GrantAsync(id, Currency.Coins, 1, System(CurrencyReasons.EventReward, "event:9"));
        var second = await service.GrantAsync(id, Currency.Coins, 500, key);

        Assert.True(second.Replayed);
        Assert.Equal(first.TransactionId, second.TransactionId);
        Assert.Equal(first.PublicId, second.PublicId);
        // The stored entries, not a recomputation…
        Assert.Equal(500, second.Entries.Single().BalanceAfter);
        // …and the balance as it is now.
        Assert.Equal(501, second.Balances[id].Coins);
        Assert.Equal(501, (await ReloadAsync(id)).Coins);
        Assert.Equal(2, (await TransactionsAsync()).Count);
    }

    [Fact]
    public async Task SameKeyDifferentRequest_IsIdempotencyKeyReuse()
    {
        var id = await SeedUserAsync(coins: 0);
        await using var ctx = NewContext();
        var service = Service(ctx);

        await service.GrantAsync(id, Currency.Coins, 500, System(CurrencyReasons.EventReward, "event:1"));
        var ex = await Assert.ThrowsAsync<CurrencyException>(() =>
            service.GrantAsync(id, Currency.Coins, 501, System(CurrencyReasons.EventReward, "event:1")));

        Assert.Equal(CurrencyErrorCode.IdempotencyKeyReuse, ex.Code);
        Assert.Equal(500, (await ReloadAsync(id)).Coins);
    }

    [Fact]
    public async Task KeysAreUniquePerScope()
    {
        var id = await SeedUserAsync(coins: 0);
        await using var ctx = NewContext();
        var service = Service(ctx);

        await service.GrantAsync(id, Currency.Coins, 5, System(CurrencyReasons.EventReward, "abc"));
        var other = await service.GrantAsync(id, Currency.Coins, 5, System(CurrencyReasons.EventReward, "abc") with { IdempotencyScope = CurrencyIdempotencyScopes.Plugin });

        Assert.False(other.Replayed);
        Assert.Equal(10, (await ReloadAsync(id)).Coins);
    }

    // ===== Admin add / remove / set =====

    [Fact]
    public async Task AdminSet_ComputesTheDeltaServerSide()
    {
        var id = await SeedUserAsync(coins: 730);
        await using var ctx = NewContext();
        var service = Service(ctx);

        var down = await service.AdminAdjustAsync(new AdminAdjustRequest(id, Currency.Coins, CurrencyOperation.Set, 200), Staff(CurrencyReasons.AdminSet, "set-1"));
        var entry = down.Entries.Single();
        Assert.Equal(("Set", -530L, 730L, 200L), (entry.Operation, entry.Amount, entry.BalanceBefore, entry.BalanceAfter));

        // Setting to the current value is recorded (amount 0) so the action stays traceable.
        var same = await service.AdminAdjustAsync(new AdminAdjustRequest(id, Currency.Coins, CurrencyOperation.Set, 200), Staff(CurrencyReasons.AdminSet, "set-2"));
        Assert.Equal(0, same.Entries.Single().Amount);

        var txs = await TransactionsAsync();
        Assert.Equal(2, txs.Count);
        Assert.All(txs, t => Assert.Equal(0, t.Entries.Sum(e => e.Amount)));
        Assert.Equal((CurrencyInitiator.Admin, (int?)999, "Compensation for a bug"), (txs[0].Initiator, txs[0].InitiatorUserId, txs[0].Reason));
        Assert.Equal(200, (await ReloadAsync(id)).Coins);
    }

    [Fact]
    public async Task AdminSet_WithAStaleExpectedCurrent_IsRefused()
    {
        var id = await SeedUserAsync(coins: 730);
        await using var ctx = NewContext();

        var ex = await Assert.ThrowsAsync<CurrencyException>(() => Service(ctx).AdminAdjustAsync(
            new AdminAdjustRequest(id, Currency.Coins, CurrencyOperation.Set, 0, ExpectedCurrent: 700), Staff(CurrencyReasons.AdminSet, "set-1")));

        Assert.Equal(CurrencyErrorCode.ExpectedBalanceMismatch, ex.Code);
        Assert.Equal(730, (await ReloadAsync(id)).Coins);
    }

    [Fact]
    public async Task AdminAdjust_Rules()
    {
        var id = await SeedUserAsync(gems: 3);
        await using var ctx = NewContext();
        var service = Service(ctx);

        async Task<CurrencyErrorCode> Code(AdminAdjustRequest r, CurrencyContext c) =>
            (await Assert.ThrowsAsync<CurrencyException>(() => service.AdminAdjustAsync(r, c))).Code;

        var take = new AdminAdjustRequest(id, Currency.Gems, CurrencyOperation.Remove, 4);
        Assert.Equal(CurrencyErrorCode.InsufficientFunds, await Code(take, Staff(CurrencyReasons.AdminTake, "a1")));
        Assert.Equal(CurrencyErrorCode.InvalidRequest, await Code(take, Staff(CurrencyReasons.AdminGrant, "a2")));
        Assert.Equal(CurrencyErrorCode.InvalidRequest, await Code(take, Staff(CurrencyReasons.AdminTake, "a3", note: " ")));
        Assert.Equal(CurrencyErrorCode.InvalidRequest, await Code(take, System(CurrencyReasons.AdminTake, "a4") with { Reason = "x" }));
        Assert.Equal(CurrencyErrorCode.AmountOutOfRange,
            await Code(new AdminAdjustRequest(id, Currency.Gems, CurrencyOperation.Set, BalanceLimits.MaxGems + 1L), Staff(CurrencyReasons.AdminSet, "a5")));

        var ok = await service.AdminAdjustAsync(take with { Amount = 3 }, Staff(CurrencyReasons.AdminTake, "a6"));
        Assert.Equal(("Remove", -3L), (ok.Entries.Single().Operation, ok.Entries.Single().Amount));
    }

    // ===== Reversals =====

    [Fact]
    public async Task Reverse_UndoesATransaction_OnceOnly()
    {
        var id = await SeedUserAsync(coins: 0);
        await using var ctx = NewContext();
        var service = Service(ctx);
        var grant = await service.AdminAdjustAsync(new AdminAdjustRequest(id, Currency.Coins, CurrencyOperation.Add, 1_000), Staff(CurrencyReasons.AdminGrant, "g1"));

        var reversal = await service.ReverseAsync(grant.TransactionId, new ReversalOptions(), Staff(CurrencyReasons.Reversal, $"reverse:{grant.TransactionId}", "Granted to the wrong player"));

        Assert.Equal(0, (await ReloadAsync(id)).Coins);
        var stored = (await TransactionsAsync()).Single(t => t.Id == reversal.TransactionId);
        Assert.Equal((CurrencyTransactionKind.Reversal, (long?)grant.TransactionId), (stored.Kind, stored.ReversesTransactionId));
        Assert.Equal(1_000, stored.Entries.Single(e => e.AccountKind == CurrencyAccountKind.System).Amount);
        Assert.Equal("SYS_ADMIN", stored.Entries.Single(e => e.AccountKind == CurrencyAccountKind.System).SystemAccount);

        // Same key: replay. Another key: already reversed.
        Assert.True((await service.ReverseAsync(grant.TransactionId, new ReversalOptions(), Staff(CurrencyReasons.Reversal, $"reverse:{grant.TransactionId}", "Granted to the wrong player"))).Replayed);
        var again = await Assert.ThrowsAsync<CurrencyException>(() =>
            service.ReverseAsync(grant.TransactionId, new ReversalOptions(), Staff(CurrencyReasons.Reversal, "other-key", "again")));
        Assert.Equal(CurrencyErrorCode.AlreadyReversed, again.Code);

        var ofReversal = await Assert.ThrowsAsync<CurrencyException>(() =>
            service.ReverseAsync(reversal.TransactionId, new ReversalOptions(), Staff(CurrencyReasons.Reversal, "r3", "undo the undo")));
        Assert.Equal(CurrencyErrorCode.NotReversible, ofReversal.Code);

        var missing = await Assert.ThrowsAsync<CurrencyException>(() =>
            service.ReverseAsync(987654, new ReversalOptions(), Staff(CurrencyReasons.Reversal, "r4", "nothing")));
        Assert.Equal(CurrencyErrorCode.TransactionNotFound, missing.Code);
    }

    [Fact]
    public async Task Reverse_OfSpentMoney_NeedsAllowPartial()
    {
        var id = await SeedUserAsync(coins: 0);
        await using var ctx = NewContext();
        var service = Service(ctx);
        var grant = await service.GrantAsync(id, Currency.Coins, 1_000, System(CurrencyReasons.EventReward, "event:1"));
        await service.SpendAsync(id, Currency.Coins, 700, System(CurrencyReasons.TeleportFee, "tp-1"));

        var refused = await Assert.ThrowsAsync<CurrencyException>(() =>
            service.ReverseAsync(grant.TransactionId, new ReversalOptions(), Staff(CurrencyReasons.Reversal, "r1", "Event was rigged")));
        Assert.Equal(CurrencyErrorCode.ReversalWouldGoNegative, refused.Code);
        Assert.Equal(300, (await ReloadAsync(id)).Coins);

        var partial = await service.ReverseAsync(grant.TransactionId, new ReversalOptions(AllowPartial: true), Staff(CurrencyReasons.Reversal, "r2", "Event was rigged"));

        Assert.Equal(-300, partial.Entries.Single().Amount);
        Assert.Equal(0, (await ReloadAsync(id)).Coins);
        var stored = (await TransactionsAsync()).Single(t => t.Id == partial.TransactionId);
        Assert.Equal(0, stored.Entries.Sum(e => e.Amount));
        var shortfall = JsonDocument.Parse(stored.MetadataJson!).RootElement.GetProperty("shortfall")[0];
        Assert.Equal(700, shortfall.GetProperty("amount").GetInt64());
    }

    // ===== Reconciliation and history =====

    [Fact]
    public async Task Reconciler_IsCleanAfterPostings_AndSpotsAWriteOutsideTheLedger()
    {
        var a = await SeedUserAsync("a", coins: 40);
        var b = await SeedUserAsync("b", coins: 0);
        await SeedUserAsync("no-ledger-rows", coins: 12345);
        await using (var ctx = NewContext())
        {
            var service = Service(ctx);
            await service.GrantAsync(a, Currency.Coins, 60, System(CurrencyReasons.EventReward, "e1"));
            await service.SpendAsync(a, Currency.Coins, 30, System(CurrencyReasons.TeleportFee, "t1"));
            await service.PostAsync(new[] { new CurrencyLeg(a, Currency.Gems, 2), new CurrencyLeg(b, Currency.Experience, 9) }, System(CurrencyReasons.SiegeReward, "s1"));
            await service.AdminAdjustAsync(new AdminAdjustRequest(b, Currency.Experience, CurrencyOperation.Set, 3), Staff(CurrencyReasons.AdminSet, "x1"));

            Assert.Empty(await new CurrencyReconciler(ctx).FindMismatchesAsync());
        }

        await using (var ctx = NewContext())
        {
            var user = await ctx.Users.SingleAsync(u => u.Id == a);
            user.Coins += 5; // a pre-ledger write path
            await ctx.SaveChangesAsync();

            var mismatch = Assert.Single(await new CurrencyReconciler(ctx).FindMismatchesAsync());
            Assert.Equal(("BalanceColumn", a, "Coins", (long?)70, (long?)75), (mismatch.Kind, mismatch.UserId, mismatch.Currency, mismatch.Expected, mismatch.Actual));
            Assert.Empty(await new CurrencyReconciler(ctx).FindMismatchesAsync(b));

            // The next posting starts from the changed balance, so the chain shows the gap.
            await Service(ctx).GrantAsync(a, Currency.Coins, 1, System(CurrencyReasons.EventReward, "e2"));
            var chain = Assert.Single(await new CurrencyReconciler(ctx).FindMismatchesAsync());
            Assert.Equal(("Chain", (long?)70, (long?)75), (chain.Kind, chain.Expected, chain.Actual));
        }
    }

    [Fact]
    public async Task History_FiltersSortsAndPages()
    {
        var a = await SeedUserAsync("alice", coins: 0);
        var b = await SeedUserAsync("bob", coins: 0);
        await using var ctx = NewContext();
        var service = Service(ctx);
        for (var i = 1; i <= 5; i++)
        {
            await service.GrantAsync(a, Currency.Coins, i * 10, System(CurrencyReasons.EventReward, $"e{i}"));
        }
        await service.GrantAsync(b, Currency.Gems, 3, CurrencyContext.ForSystem("DomainDiscovery", CurrencyReasons.DiscoveryReward, "d1"));
        await service.AdminAdjustAsync(new AdminAdjustRequest(b, Currency.Coins, CurrencyOperation.Add, 7), Staff(CurrencyReasons.AdminGrant, "g1"));

        var page = await service.GetHistoryAsync(new LedgerQuery { UserId = a, PageSize = 2, Page = 2 });
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(new long[] { 30, 20 }, page.Items.Select(l => l.Amount));
        Assert.All(page.Items, l => Assert.Equal("alice", l.Username));

        var byAmount = await service.GetHistoryAsync(new LedgerQuery { Sort = LedgerSort.Amount, Descending = false, Currency = Currency.Coins });
        Assert.Equal(new long[] { 7, 10, 20, 30, 40, 50 }, byAmount.Items.Select(l => l.Amount));

        var discovery = await service.GetHistoryAsync(new LedgerQuery { InitiatorSearch = "discov" });
        var line = Assert.Single(discovery.Items);
        Assert.Equal(("bob", "Gems", "DISCOVERY_REWARD", "System", 10L, 13L), (line.Username, line.Currency, line.ReasonCode, line.Initiator, line.BalanceBefore, line.BalanceAfter));

        var staff = await service.GetHistoryAsync(new LedgerQuery { Initiator = CurrencyInitiator.Admin, UserSearch = "BO" });
        Assert.Equal((999, "Compensation for a bug"), (Assert.Single(staff.Items).InitiatorUserId!.Value, staff.Items[0].Reason));

        Assert.Equal(200, (await service.GetHistoryAsync(new LedgerQuery { PageSize = 10_000 })).PageSize);
    }

    [Fact]
    public async Task GetBalances()
    {
        var id = await SeedUserAsync(coins: 1, gems: 2, xp: 3);
        await using var ctx = NewContext();
        var balances = await Service(ctx).GetBalancesAsync(id);
        Assert.Equal((1, 2, 3), (balances.Coins, balances.Gems, balances.ExperiencePoints));
        Assert.Equal(CurrencyErrorCode.UserNotFound, (await Assert.ThrowsAsync<CurrencyException>(() => Service(ctx).GetBalancesAsync(4242))).Code);
    }

    // ===== Append-only =====

    [Fact]
    public void LedgerRepository_HasNoUpdateOrDeleteMethods()
    {
        var names = typeof(ICurrencyRepository).GetMethods().Select(m => m.Name).ToList();
        Assert.DoesNotContain(names, n => n.StartsWith("Update") || n.StartsWith("Delete") || n.StartsWith("Remove") || n.StartsWith("Set"));
    }

    [Fact]
    public void PublicIds_AreSortableUlids()
    {
        var earlier = CurrencyIds.NewPublicId(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000));
        var later = CurrencyIds.NewPublicId(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_001));
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{26}$", earlier);
        Assert.True(string.CompareOrdinal(earlier, later) < 0);
        Assert.NotEqual(CurrencyIds.NewPublicId(), CurrencyIds.NewPublicId());
    }
}
