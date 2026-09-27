using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Player transfers through the ledger (currency-payments IMPLEMENTATION_PLAN.md Phase 3):
/// the two-leg PLAYER_TRANSFER posting, idempotent retries, the confirmation lifecycle
/// (create, confirm, double confirm, cancel, expiry), limits, leaderboard exclusions and the
/// counterparty on history lines. EF InMemory: no row locks — concurrency is proven in
/// Tests/MySql/PlayerTransferMySqlTests.cs (requires-mysql).
/// </summary>
public class CurrencyTransferServiceTests
{
    private readonly string _db = $"transfers-{Guid.NewGuid()}";

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_db).Options);

    private static CurrencyService Service(KnKDbContext ctx) =>
        new(new CurrencyRepository(ctx), new UserRepository(ctx), NullLogger<CurrencyService>.Instance);

    public CurrencyTransferServiceTests()
    {
        using var ctx = NewContext();
        ctx.CurrencyPolicies.Add(TransferPolicyEvaluatorTests.CoinPolicy());
        ctx.CurrencyPolicies.Add(TransferPolicyEvaluatorTests.GemPolicy());
        ctx.TitleBrackets.Add(new TitleBracket { Id = 1, MaleName = "Peasant", FemaleName = "Peasant", MinExperience = 2500 });
        ctx.SaveChanges();
    }

    private async Task<int> SeedUserAsync(string name, int coins = 0, int gems = 0, int xp = 5000, double ageHours = 100,
        bool locked = false, bool active = true)
    {
        await using var ctx = NewContext();
        var user = new User
        {
            Username = name, Uuid = Guid.NewGuid().ToString(), Coins = coins, Gems = gems, ExperiencePoints = xp,
            CreatedAt = DateTime.UtcNow.AddHours(-ageHours), TransferLockReason = locked ? "test lock" : null, IsActive = active
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private static CurrencyContext Pay(int sender, string key) => new()
    {
        IdempotencyKey = key,
        IdempotencyScope = CurrencyIdempotencyScopes.Plugin,
        ReasonCode = CurrencyReasons.PlayerTransfer,
        Initiator = CurrencyInitiator.Player,
        InitiatorUserId = sender,
        InitiatorComponent = "PluginPayCommand"
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

    private async Task SetPolicyAsync(Currency currency, Action<CurrencyPolicy> change)
    {
        await using var ctx = NewContext();
        var policy = await ctx.CurrencyPolicies.SingleAsync(p => p.Currency == currency);
        change(policy);
        await ctx.SaveChangesAsync();
    }

    // ===== Completed transfers =====

    [Fact]
    public async Task Transfer_PostsOnePlayerTransferWithTwoLegs()
    {
        var alice = await SeedUserAsync("alice", coins: 5000);
        var bob = await SeedUserAsync("bob", coins: 100);
        await using var ctx = NewContext();

        var result = await Service(ctx).TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 1200), Pay(alice, "pay-1"));

        Assert.Equal(TransferResultDto.StatusCompleted, result.Status);
        Assert.False(result.Replayed);
        Assert.Equal(("alice", "bob", 1200L, 0L), (result.SenderUsername, result.RecipientUsername, result.Amount, result.Fee));
        Assert.Equal(3800, result.SenderBalances!.Coins);
        Assert.Equal(1300, result.RecipientBalanceAfter);
        Assert.Equal((3800, 1300), ((await ReloadAsync(alice)).Coins, (await ReloadAsync(bob)).Coins));

        var tx = Assert.Single(await TransactionsAsync());
        Assert.Equal((CurrencyTransactionKind.Transfer, "PLAYER_TRANSFER", CurrencyInitiator.Player, (int?)alice),
            (tx.Kind, tx.ReasonCode, tx.Initiator, tx.InitiatorUserId));
        Assert.Equal(((int?)alice, (int?)bob), (tx.FromUserId, tx.ToUserId));
        Assert.Equal(2, tx.Entries.Count);
        Assert.All(tx.Entries, e => Assert.Equal(CurrencyAccountKind.User, e.AccountKind));
        Assert.Equal(0, tx.Entries.Sum(e => e.Amount));
        Assert.Equal((-1200L, 5000L, 3800L), tx.Entries.Where(e => e.UserId == alice).Select(e => (e.Amount, e.BalanceBefore!.Value, e.BalanceAfter!.Value)).Single());

        await using var check = NewContext();
        Assert.Empty(await new CurrencyReconciler(check).FindMismatchesAsync(alice));
        Assert.Empty(await new CurrencyReconciler(check).FindMismatchesAsync(bob));
    }

    [Fact]
    public async Task ReplayedKey_PaysOnce_AndReturnsTheSameTransaction()
    {
        var alice = await SeedUserAsync("alice", coins: 5000);
        var bob = await SeedUserAsync("bob");
        await using var ctx = NewContext();
        var service = Service(ctx);
        var request = new TransferRequest(alice, bob, Currency.Coins, 1000);

        var first = await service.TransferAsync(request, Pay(alice, "pay-once"));
        var retry = await service.TransferAsync(request, Pay(alice, "pay-once"));

        Assert.True(retry.Replayed);
        Assert.Equal(first.PublicId, retry.PublicId);
        Assert.Equal(4000, (await ReloadAsync(alice)).Coins);
        Assert.Single(await TransactionsAsync());

        var reuse = await Assert.ThrowsAsync<CurrencyException>(() =>
            service.TransferAsync(request with { Amount = 999 }, Pay(alice, "pay-once")));
        Assert.Equal(CurrencyErrorCode.IdempotencyKeyReuse, reuse.Code);
    }

    [Fact]
    public async Task Fee_GoesToSysFees_OnTopOfTheAmount()
    {
        await SetPolicyAsync(Currency.Coins, p => p.TransferFeeBasisPoints = 200);
        var alice = await SeedUserAsync("alice", coins: 5000);
        var bob = await SeedUserAsync("bob");
        await using var ctx = NewContext();

        var result = await Service(ctx).TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 1000), Pay(alice, "fee-1"));

        Assert.Equal(20, result.Fee);
        Assert.Equal((3980, 1000), ((await ReloadAsync(alice)).Coins, (await ReloadAsync(bob)).Coins));
        var tx = Assert.Single(await TransactionsAsync());
        Assert.Equal(("SYS_FEES", 20L), tx.Entries.Where(e => e.AccountKind == CurrencyAccountKind.System).Select(e => (e.SystemAccount!, e.Amount)).Single());
        Assert.Equal(0, tx.Entries.Sum(e => e.Amount));
        await using var check = NewContext();
        Assert.Empty(await new CurrencyReconciler(check).FindMismatchesAsync(alice));
    }

    [Fact]
    public async Task PartialReversal_OfATransferWithAFee_IsRefused_NotFundedFromSysFees()
    {
        // Bob spent most of what Alice sent. Balancing Bob's shortfall against SYS_FEES would
        // refund Alice in full while Bob returns only part: the difference would be minted.
        await SetPolicyAsync(Currency.Coins, p => p.TransferFeeBasisPoints = 200);
        var alice = await SeedUserAsync("alice", coins: 5000);
        var bob = await SeedUserAsync("bob");
        await using var ctx = NewContext();
        var service = Service(ctx);
        var sent = await service.TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 1000), Pay(alice, "fee-rev-1"));
        await service.SpendAsync(bob, Currency.Coins, 600, CurrencyContext.ForSystem("Test", CurrencyReasons.KitPurchase, "kit-purchase:9:" + bob));

        var reversal = new CurrencyContext
        {
            IdempotencyKey = $"reverse:{sent.TransactionId}",
            IdempotencyScope = CurrencyIdempotencyScopes.Web,
            ReasonCode = CurrencyReasons.Reversal,
            Reason = "Scam reported by alice",
            Initiator = CurrencyInitiator.Admin,
            InitiatorUserId = 999,
            InitiatorComponent = "Test"
        };
        var ex = await Assert.ThrowsAsync<CurrencyException>(() =>
            service.ReverseAsync(sent.TransactionId!.Value, new ReversalOptions(AllowPartial: true), reversal));

        Assert.Equal(CurrencyErrorCode.ReversalWouldGoNegative, ex.Code);
        Assert.Equal((3980, 400), ((await ReloadAsync(alice)).Coins, (await ReloadAsync(bob)).Coins));
        Assert.Equal(2, (await TransactionsAsync()).Count);

        // With the money still there the full reversal works and refunds the fee too.
        await service.GrantAsync(bob, Currency.Coins, 600, CurrencyContext.ForSystem("Test", CurrencyReasons.EventReward, "event:fee-rev:" + bob));
        var full = await service.ReverseAsync(sent.TransactionId!.Value, new ReversalOptions(), reversal with { IdempotencyKey = "reverse-full" });
        Assert.False(full.Replayed);
        Assert.Equal((5000, 0), ((await ReloadAsync(alice)).Coins, (await ReloadAsync(bob)).Coins));
        await using var check = NewContext();
        Assert.Empty(await new CurrencyReconciler(check).FindMismatchesAsync());
    }

    [Fact]
    public async Task RefusedTransfer_WritesNothing_AndARetryIsEvaluatedAgain()
    {
        var alice = await SeedUserAsync("alice", coins: 500);
        var bob = await SeedUserAsync("bob");
        await using var ctx = NewContext();
        var service = Service(ctx);

        var ex = await Assert.ThrowsAsync<CurrencyException>(() =>
            service.TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 600), Pay(alice, "k")));
        Assert.Equal(CurrencyErrorCode.InsufficientFunds, ex.Code);
        Assert.Empty(await TransactionsAsync());
    }

    [Fact]
    public async Task Gems_AreRefused_WhateverTheAmount()
    {
        var alice = await SeedUserAsync("alice", gems: 50);
        var bob = await SeedUserAsync("bob");
        await using var ctx = NewContext();

        var ex = await Assert.ThrowsAsync<CurrencyException>(() =>
            Service(ctx).TransferAsync(new TransferRequest(alice, bob, Currency.Gems, 5), Pay(alice, "g")));
        Assert.Equal(CurrencyErrorCode.NotTransferable, ex.Code);
        Assert.Equal(50, (await ReloadAsync(alice)).Gems);
    }

    [Fact]
    public async Task UnknownOrLockedRecipient_AndYoungSender_AreRefused()
    {
        var alice = await SeedUserAsync("alice", coins: 5000);
        var locked = await SeedUserAsync("locked", locked: true);
        var young = await SeedUserAsync("young", coins: 5000, ageHours: 2);
        var bob = await SeedUserAsync("bob");
        await using var ctx = NewContext();
        var service = Service(ctx);

        Assert.Equal(CurrencyErrorCode.RecipientNotFound, (await Assert.ThrowsAsync<CurrencyException>(() =>
            service.TransferAsync(new TransferRequest(alice, 99_999, Currency.Coins, 100), Pay(alice, "a")))).Code);
        Assert.Equal(CurrencyErrorCode.AccountLocked, (await Assert.ThrowsAsync<CurrencyException>(() =>
            service.TransferAsync(new TransferRequest(alice, locked, Currency.Coins, 100), Pay(alice, "b")))).Code);
        Assert.Equal(CurrencyErrorCode.NewAccountRestricted, (await Assert.ThrowsAsync<CurrencyException>(() =>
            service.TransferAsync(new TransferRequest(young, bob, Currency.Coins, 100), Pay(young, "c")))).Code);
        Assert.Equal(CurrencyErrorCode.SelfTransfer, (await Assert.ThrowsAsync<CurrencyException>(() =>
            service.TransferAsync(new TransferRequest(alice, alice, Currency.Coins, 100), Pay(alice, "d")))).Code);
    }

    [Fact]
    public async Task DailyCap_CountsEarlierTransfers()
    {
        await SetPolicyAsync(Currency.Coins, p => { p.DailySendCap = 1000; p.CooldownSeconds = 0; });
        var alice = await SeedUserAsync("alice", coins: 5000);
        var bob = await SeedUserAsync("bob");
        await using var ctx = NewContext();
        var service = Service(ctx);

        await service.TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 700), Pay(alice, "d1"));
        var ex = await Assert.ThrowsAsync<CurrencyException>(() =>
            service.TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 301), Pay(alice, "d2")));
        Assert.Equal(CurrencyErrorCode.DailyCapExceeded, ex.Code);
        await service.TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 300), Pay(alice, "d3"));
        Assert.Equal(1000, (await ReloadAsync(bob)).Coins);
    }

    [Fact]
    public async Task ReversedTransfer_StopsCountingTowardTheCapsAndTheHourlyLimit()
    {
        // KNG-21 developer decision: a reversed transfer frees the sender's daily send cap and
        // hourly count and the recipient's daily receive cap.
        await SetPolicyAsync(Currency.Coins, p => { p.DailySendCap = 1000; p.DailyReceiveCap = 1000; p.CooldownSeconds = 0; p.MaxTransfersPerHour = 1; });
        var alice = await SeedUserAsync("alice", coins: 5000);
        var bob = await SeedUserAsync("bob");
        var carol = await SeedUserAsync("carol", coins: 5000);
        var dave = await SeedUserAsync("dave", coins: 5000);
        await using var ctx = NewContext();
        var service = Service(ctx);

        var sent = await service.TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 1000), Pay(alice, "r1"));
        Assert.Equal(CurrencyErrorCode.CooldownActive, (await Assert.ThrowsAsync<CurrencyException>(() =>
            service.TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 10), Pay(alice, "r2")))).Code);
        Assert.Equal(CurrencyErrorCode.RecipientDailyCapExceeded, (await Assert.ThrowsAsync<CurrencyException>(() =>
            service.TransferAsync(new TransferRequest(carol, bob, Currency.Coins, 10), Pay(carol, "r3")))).Code);

        await service.ReverseAsync(sent.TransactionId!.Value, new ReversalOptions(), new CurrencyContext
        {
            IdempotencyKey = $"reverse:{sent.TransactionId}",
            IdempotencyScope = CurrencyIdempotencyScopes.Web,
            ReasonCode = CurrencyReasons.Reversal,
            Reason = "Scam reported by alice",
            Initiator = CurrencyInitiator.Admin,
            InitiatorUserId = 999,
            InitiatorComponent = "Test"
        });

        var limits = await service.GetTransferLimitsAsync(alice, Currency.Coins);
        Assert.Equal((0L, 1000L, (DateTime?)null), (limits.SentLast24h, limits.RemainingToday, limits.NextTransferAt));
        await service.TransferAsync(new TransferRequest(carol, bob, Currency.Coins, 400), Pay(carol, "r4"));
        await service.TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 600), Pay(alice, "r5"));
        Assert.Equal(1000, (await ReloadAsync(bob)).Coins);
        Assert.Equal(CurrencyErrorCode.RecipientDailyCapExceeded, (await Assert.ThrowsAsync<CurrencyException>(() =>
            service.TransferAsync(new TransferRequest(dave, bob, Currency.Coins, 10), Pay(dave, "r6")))).Code);
    }

    [Fact]
    public async Task Cooldown_RefusesAnImmediateSecondSend()
    {
        var alice = await SeedUserAsync("alice", coins: 5000);
        var bob = await SeedUserAsync("bob");
        await using var ctx = NewContext();
        var service = Service(ctx);

        await service.TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 100), Pay(alice, "c1"));
        var ex = await Assert.ThrowsAsync<CurrencyException>(() =>
            service.TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 100), Pay(alice, "c2")));
        Assert.Equal(CurrencyErrorCode.CooldownActive, ex.Code);
    }

    [Fact]
    public async Task TheInitiatorMustBeTheSender()
    {
        var alice = await SeedUserAsync("alice", coins: 5000);
        var bob = await SeedUserAsync("bob");
        await using var ctx = NewContext();

        var ex = await Assert.ThrowsAsync<CurrencyException>(() =>
            Service(ctx).TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 100), Pay(bob, "x")));
        Assert.Equal(CurrencyErrorCode.InvalidRequest, ex.Code);
        Assert.Equal(5000, (await ReloadAsync(alice)).Coins);
    }

    // ===== Confirmation =====

    [Fact]
    public async Task LargeTransfer_WaitsForConfirmation_ThenMovesOnConfirm()
    {
        var alice = await SeedUserAsync("alice", coins: 500_000);
        var bob = await SeedUserAsync("bob");
        await using var ctx = NewContext();
        var service = Service(ctx);

        var created = await service.TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 150_000), Pay(alice, "big-1"));

        Assert.Equal(TransferResultDto.StatusPendingConfirmation, created.Status);
        Assert.Equal("Pending", created.Pending!.Status);
        Assert.InRange(created.Pending.ExpiresInSeconds, 55, 60);
        Assert.Equal(500_000, (await ReloadAsync(alice)).Coins);
        Assert.Empty(await TransactionsAsync());

        // Retrying the /pay itself returns the same pending transfer.
        var retried = await service.TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 150_000), Pay(alice, "big-1"));
        Assert.Equal(created.Pending.PublicId, retried.Pending!.PublicId);

        var confirmed = await service.ConfirmTransferAsync(created.Pending.PublicId, alice, Pay(alice, "ignored"));
        Assert.Equal(TransferResultDto.StatusCompleted, confirmed.Status);
        Assert.Equal((350_000, 150_000), ((await ReloadAsync(alice)).Coins, (await ReloadAsync(bob)).Coins));

        var tx = Assert.Single(await TransactionsAsync());
        Assert.Equal(("PendingTransfer", created.Pending.PublicId, $"transfer-confirm:{created.Pending.PublicId}"),
            (tx.SourceType, tx.SourceRef, tx.IdempotencyKey));

        // Double confirm → the same result, nothing moves again.
        var again = await service.ConfirmTransferAsync(created.Pending.PublicId, alice, Pay(alice, "ignored"));
        Assert.True(again.Replayed);
        Assert.Equal(confirmed.PublicId, again.PublicId);
        Assert.Single(await TransactionsAsync());

        // And the original /pay retried after confirming reports the completed payment.
        var late = await service.TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 150_000), Pay(alice, "big-1"));
        Assert.Equal((TransferResultDto.StatusCompleted, confirmed.PublicId), (late.Status, late.PublicId));
    }

    [Fact]
    public async Task CancelThenConfirm_IsAConflict()
    {
        var alice = await SeedUserAsync("alice", coins: 500_000);
        var bob = await SeedUserAsync("bob");
        await using var ctx = NewContext();
        var service = Service(ctx);
        var created = await service.TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 200_000), Pay(alice, "big-2"));

        var cancelled = await service.CancelTransferAsync(created.Pending!.PublicId, alice);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal("Cancelled", (await service.CancelTransferAsync(created.Pending.PublicId, alice)).Status);

        var ex = await Assert.ThrowsAsync<CurrencyException>(() => service.ConfirmTransferAsync(created.Pending.PublicId, alice, Pay(alice, "x")));
        Assert.Equal(CurrencyErrorCode.PendingTransferClosed, ex.Code);
        Assert.Equal(500_000, (await ReloadAsync(alice)).Coins);
    }

    [Fact]
    public async Task ExpiredPending_CantBeConfirmed()
    {
        var alice = await SeedUserAsync("alice", coins: 500_000);
        var bob = await SeedUserAsync("bob");
        await using var ctx = NewContext();
        var service = Service(ctx);
        var created = await service.TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 200_000), Pay(alice, "big-3"));
        await using (var edit = NewContext())
        {
            var row = await edit.CurrencyPendingTransfers.SingleAsync();
            row.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
            await edit.SaveChangesAsync();
        }

        await using var fresh = NewContext();
        var ex = await Assert.ThrowsAsync<CurrencyException>(() => Service(fresh).ConfirmTransferAsync(created.Pending!.PublicId, alice, Pay(alice, "x")));
        Assert.Equal(CurrencyErrorCode.PendingTransferExpired, ex.Code);
        await using var check = NewContext();
        Assert.Equal(CurrencyPendingTransferStatus.Expired, (await check.CurrencyPendingTransfers.SingleAsync()).Status);
        Assert.Equal(500_000, (await ReloadAsync(alice)).Coins);
    }

    [Fact]
    public async Task SomeoneElsesPending_IsNotFound()
    {
        var alice = await SeedUserAsync("alice", coins: 500_000);
        var bob = await SeedUserAsync("bob", coins: 500_000);
        await using var ctx = NewContext();
        var service = Service(ctx);
        var created = await service.TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 200_000), Pay(alice, "big-4"));

        Assert.Equal(CurrencyErrorCode.PendingTransferNotFound, (await Assert.ThrowsAsync<CurrencyException>(() =>
            service.ConfirmTransferAsync(created.Pending!.PublicId, bob, Pay(bob, "x")))).Code);
        Assert.Equal(CurrencyErrorCode.PendingTransferNotFound, (await Assert.ThrowsAsync<CurrencyException>(() =>
            service.CancelTransferAsync(created.Pending!.PublicId, bob))).Code);
    }

    [Fact]
    public async Task Confirm_RechecksTheRules()
    {
        var alice = await SeedUserAsync("alice", coins: 500_000);
        var bob = await SeedUserAsync("bob");
        await using var ctx = NewContext();
        var service = Service(ctx);
        var created = await service.TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 200_000), Pay(alice, "big-5"));
        await SetPolicyAsync(Currency.Coins, p => p.TransfersEnabled = false);

        await using var fresh = NewContext();
        var ex = await Assert.ThrowsAsync<CurrencyException>(() => Service(fresh).ConfirmTransferAsync(created.Pending!.PublicId, alice, Pay(alice, "x")));
        Assert.Equal(CurrencyErrorCode.TransfersDisabled, ex.Code);
        Assert.Equal(500_000, (await ReloadAsync(alice)).Coins);
    }

    [Fact]
    public async Task ANewLargeTransfer_ReplacesTheSendersOpenOne()
    {
        var alice = await SeedUserAsync("alice", coins: 900_000);
        var bob = await SeedUserAsync("bob");
        await using var ctx = NewContext();
        var service = Service(ctx);
        var first = await service.TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 200_000), Pay(alice, "big-6"));
        await service.TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 300_000), Pay(alice, "big-7"));

        var ex = await Assert.ThrowsAsync<CurrencyException>(() => service.ConfirmTransferAsync(first.Pending!.PublicId, alice, Pay(alice, "x")));
        Assert.Equal(CurrencyErrorCode.PendingTransferClosed, ex.Code);
    }

    // ===== Limits, leaderboard, history =====

    [Fact]
    public async Task Limits_ReportWhatIsLeftAndEligibility()
    {
        await SetPolicyAsync(Currency.Coins, p => p.CooldownSeconds = 0);
        var alice = await SeedUserAsync("alice", coins: 5000);
        var young = await SeedUserAsync("young", xp: 0, ageHours: 1);
        var bob = await SeedUserAsync("bob");
        await using var ctx = NewContext();
        var service = Service(ctx);
        await service.TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 1500), Pay(alice, "l1"));

        var limits = await service.GetTransferLimitsAsync(alice, Currency.Coins);
        Assert.True(limits.Transferable);
        Assert.True(limits.Eligible);
        Assert.Equal((1500L, 1_998_500L, 100_000L), (limits.SentLast24h, limits.RemainingToday, limits.ConfirmThreshold));

        var youngLimits = await service.GetTransferLimitsAsync(young, Currency.Coins);
        Assert.False(youngLimits.Eligible);
        Assert.NotNull(youngLimits.EligibleFrom);
        Assert.Equal(("Peasant", 2500), (youngLimits.RequiredTitleName, youngLimits.RequiredExperience!.Value));

        Assert.False((await service.GetTransferLimitsAsync(alice, Currency.Gems)).Transferable);
    }

    [Fact]
    public async Task Leaderboard_LeavesOutLockedInactiveEmptyAndExemptAccounts()
    {
        var rich = await SeedUserAsync("rich", coins: 9000);
        var middle = await SeedUserAsync("middle", coins: 5000);
        await SeedUserAsync("locked", coins: 99_000, locked: true);
        await SeedUserAsync("gone", coins: 98_000, active: false);
        await SeedUserAsync("broke", coins: 0);
        var staff = await SeedUserAsync("staff", coins: 97_000);
        var member = await SeedUserAsync("member", coins: 96_000);
        await using (var ctx = NewContext())
        {
            var group = new PermissionGroup { Name = "Staff" };
            ctx.PermissionGroups.Add(group);
            await ctx.SaveChangesAsync();
            ctx.PermissionGrants.Add(new PermissionGrant { HolderId = staff, Node = CurrencyService.BaltopExemptNode, Value = true });
            ctx.PermissionGrants.Add(new PermissionGrant { HolderId = group.Id, Node = CurrencyService.BaltopExemptNode, Value = true });
            ctx.UserPermissionGroups.Add(new UserPermissionGroup { UserId = member, PermissionGroupId = group.Id });
            await ctx.SaveChangesAsync();
        }

        await using var read = NewContext();
        var board = await Service(read).GetLeaderboardAsync(Currency.Coins, 1, 10);

        Assert.Equal(2, board.TotalCount);
        Assert.Equal(new[] { (1, rich, 9000L), (2, middle, 5000L) }, board.Entries.Select(e => (e.Rank, e.UserId, e.Balance)));
        var page2 = await Service(read).GetLeaderboardAsync(Currency.Coins, 2, 1);
        Assert.Equal((2, "middle"), (page2.Entries.Single().Rank, page2.Entries.Single().Username));

        // Out-of-range paging is clamped (no offset overflow on a huge page number).
        var far = await Service(read).GetLeaderboardAsync(Currency.Coins, int.MaxValue, int.MaxValue);
        Assert.Equal((CurrencyService.MaxLeaderboardPage, CurrencyService.MaxLeaderboardPageSize, 2), (far.Page, far.PageSize, far.TotalCount));
        Assert.Empty(far.Entries);
    }

    [Fact]
    public async Task History_NamesTheOtherPlayerOfATransfer()
    {
        var alice = await SeedUserAsync("alice", coins: 5000);
        var bob = await SeedUserAsync("bob");
        await using var ctx = NewContext();
        var service = Service(ctx);
        await service.TransferAsync(new TransferRequest(alice, bob, Currency.Coins, 250, Note: "for the sword"), Pay(alice, "h1"));

        var aliceLine = Assert.Single((await service.GetHistoryAsync(new LedgerQuery { UserId = alice })).Items);
        var bobLine = Assert.Single((await service.GetHistoryAsync(new LedgerQuery { UserId = bob })).Items);
        Assert.Equal(("bob", -250L, "for the sword"), (aliceLine.CounterpartyUsername, aliceLine.Amount, aliceLine.Reason));
        Assert.Equal(("alice", 250L, (int?)alice), (bobLine.CounterpartyUsername, bobLine.Amount, bobLine.CounterpartyUserId));
    }
}
