using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// Currency-payments Phase 2 on a real MySQL: every coin/gem/XP write path goes through the
/// ledger. EF can't write the balance columns any more; salary, kits, staff adjustments, title
/// bonuses (once per bracket, ever), signup grants and account merges post ledger rows under the
/// user row lock, with their deterministic keys; and after a scripted session over all of them
/// the reconciler reports nothing. Uses the real title data the migrations seed (Serf 0 XP,
/// Peasant 2,500 XP: 13,500 coins / 3 gems / 32 XP bonus).
/// </summary>
[Trait("Category", "requires-mysql")]
public class LedgerRoutingMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public LedgerRoutingMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static readonly IMapper Mapper =
        new MapperConfiguration(cfg => cfg.AddMaps(typeof(UserMappingProfile).Assembly)).CreateMapper();

    /// <summary>One request's worth of services on one DbContext, wired like the DI container does.</summary>
    private sealed class Request : IAsyncDisposable
    {
        public Request(KnKDbContext ctx)
        {
            Ctx = ctx;
            UserRepo = new UserRepository(ctx);
            Currency = new CurrencyService(new CurrencyRepository(ctx), UserRepo, NullLogger<CurrencyService>.Instance);
            var audit = new AuditLogService(new AuditLogRepository(ctx), UserRepo);
            var titleService = new TitleService(new TitleBracketRepository(ctx));
            var groupRepo = new PermissionGroupRepository(ctx);
            var memberships = new UserPermissionGroupService(new UserPermissionGroupRepository(ctx), UserRepo, groupRepo, audit);
            Titles = new TitleProgressionService(Currency, UserRepo, titleService, memberships, audit);
            Users = new UserService(UserRepo, Mapper, new Mock<IPasswordService>().Object, new Mock<ILinkCodeService>().Object,
                titleService, memberships, audit, groupRepo, NullLogger<UserService>.Instance, Currency, Titles);
            var salaryConfig = new Mock<ISalaryConfigurationService>();
            salaryConfig.Setup(c => c.GetAsync()).ReturnsAsync(new SalaryConfigurationDto { GlobalMultiplier = 1m, OfflinePayoutMaxHours = 720 });
            Salary = new SalaryService(UserRepo, new UserPermissionGroupRepository(ctx), salaryConfig.Object, titleService, audit, Currency);
            Kits = new KitService(new KitRepository(ctx), UserRepo, new ItemBlueprintRepository(ctx), new TitleBracketRepository(ctx), groupRepo,
                titleService, memberships, new Mock<IPermissionResolutionService>().Object, audit, Mapper, Currency);
        }

        public KnKDbContext Ctx { get; }
        public UserRepository UserRepo { get; }
        public CurrencyService Currency { get; }
        public TitleProgressionService Titles { get; }
        public UserService Users { get; }
        public SalaryService Salary { get; }
        public KitService Kits { get; }

        public ValueTask DisposeAsync() => Ctx.DisposeAsync();
    }

    private Request NewRequest() => new(_db.NewContext());

    private static string Name(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];

    /// <summary>A user through the real signup path: inserted at 0, then SIGNUP_GRANT 250 / 50.</summary>
    private async Task<int> SignUpAsync(string prefix = "p")
    {
        await using var r = NewRequest();
        return (await r.Users.CreateAsync(new UserCreateDto { Username = Name(prefix) })).Id;
    }

    private async Task<User> ReloadAsync(int id)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    private async Task<List<CurrencyTransaction>> TransactionsForAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.CurrencyTransactions.AsNoTracking().Include(t => t.Entries)
            .Where(t => t.Entries.Any(e => e.UserId == userId))
            .OrderBy(t => t.Id).ToListAsync();
    }

    private async Task AssertReconciledAsync(params int[] userIds)
    {
        await using var ctx = _db.NewContext();
        foreach (var id in userIds)
        {
            var mismatches = await new CurrencyReconciler(ctx).FindMismatchesAsync(id);
            Assert.True(mismatches.Count == 0, $"user {id}: " + string.Join("; ", mismatches.Select(m => $"{m.Kind} {m.Currency} expected {m.Expected} actual {m.Actual}")));
        }
    }

    private static CurrencyContext Staff(string key, string reason = "Scripted staff change") => new()
    {
        IdempotencyKey = key,
        IdempotencyScope = CurrencyIdempotencyScopes.Plugin,
        ReasonCode = CurrencyReasons.AdminGrant,
        Reason = reason,
        Initiator = CurrencyInitiator.PluginService,
        InitiatorComponent = "MySqlTest"
    };

    private static CurrencyContext PluginCall(string key) => new()
    {
        IdempotencyKey = key,
        IdempotencyScope = CurrencyIdempotencyScopes.Plugin,
        ReasonCode = CurrencyReasons.KitClaimCost,
        Initiator = CurrencyInitiator.PluginService,
        InitiatorComponent = "MySqlTest"
    };

    private static List<BalanceChangeDto> Change(Currency currency, CurrencyOperation mode, long amount, long? expected = null) =>
        new() { new BalanceChangeDto { Currency = currency, Mode = mode, Amount = amount, ExpectedCurrent = expected } };

    private async Task<Kit> AddKitAsync(Action<Kit> configure)
    {
        await using var ctx = _db.NewContext();
        var kit = new Kit { Name = Name("kit") };
        configure(kit);
        ctx.Kits.Add(kit);
        await ctx.SaveChangesAsync();
        return kit;
    }

    // ===== EF can't write balances =====

    [MySqlFact]
    public async Task EfNeverWritesBalanceColumns_OnlyTheLedgerDoes()
    {
        int id;
        await using (var ctx = _db.NewContext())
        {
            var user = new User { Username = Name("ef"), Coins = 5_000, Gems = 500, ExperiencePoints = 50_000 };
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync();
            id = user.Id;
        }
        Assert.Equal((0, 0, 0), ((await ReloadAsync(id)).Coins, (await ReloadAsync(id)).Gems, (await ReloadAsync(id)).ExperiencePoints));

        await using (var ctx = _db.NewContext())
        {
            // The classic A2 shape: a full-row Update() of a detached, edited copy.
            var copy = await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == id);
            copy.Coins = 999_999;
            copy.Gems = 999;
            copy.ExperiencePoints = 1_000_000;
            copy.Username += "x";
            ctx.Users.Update(copy);
            await ctx.SaveChangesAsync();
        }
        var afterUpdate = await ReloadAsync(id);
        Assert.EndsWith("x", afterUpdate.Username);
        Assert.Equal((0, 0, 0), (afterUpdate.Coins, afterUpdate.Gems, afterUpdate.ExperiencePoints));

        await using (var r = NewRequest())
        {
            var posted = await r.Currency.GrantAsync(id, Currency.Coins, 75, CurrencyContext.ForSystem("MySqlTest", CurrencyReasons.EventReward, $"event:ef:{id}"));
            Assert.Equal(75, posted.Balances[id].Coins);
            // The tracked entity agrees with the row after the posting.
            Assert.Equal(75, (await r.UserRepo.GetByIdAsync(id))!.Coins);
        }
        Assert.Equal(75, (await ReloadAsync(id)).Coins);
        await AssertReconciledAsync(id);
    }

    // ===== Signup =====

    [MySqlFact]
    public async Task CreateAsync_PostsTheSignupGrant()
    {
        var id = await SignUpAsync("new");

        var user = await ReloadAsync(id);
        Assert.Equal((250, 50), (user.Coins, user.Gems));
        var signup = Assert.Single(await TransactionsForAsync(id));
        Assert.Equal((CurrencyReasons.SignupGrant, $"signup:{id}"), (signup.ReasonCode, signup.IdempotencyKey));
        Assert.Equal(4, signup.Entries.Count); // coins + gems user legs, SYS_SIGNUP counter legs
        await AssertReconciledAsync(id);
    }

    // ===== Salary =====

    [MySqlFact]
    public async Task SalaryCalledConcurrently_PaysOnce()
    {
        var id = await SignUpAsync("sal");
        await using (var ctx = _db.NewContext())
        {
            await ctx.Database.ExecuteSqlInterpolatedAsync($"UPDATE users SET LastSalaryPayoutAt = {DateTime.UtcNow.AddHours(-3)} WHERE Id = {id}");
        }

        var start = new TaskCompletionSource();
        var calls = Enumerable.Range(0, 6).Select(async _ =>
        {
            await start.Task;
            await using var r = NewRequest();
            return await r.Salary.PayOutAsync(id);
        }).ToList();
        start.SetResult();
        var results = await Task.WhenAll(calls);

        var paid = Assert.Single(results, x => x.Paid);
        Assert.Equal(1 + 1 / 2m + 1 / 3m, Math.Round(paid.PaidHours, 6), 3); // ~3h, log-decayed
        var salary = Assert.Single(await TransactionsForAsync(id), t => t.ReasonCode == CurrencyReasons.Salary);
        Assert.StartsWith($"salary:{id}:", salary.IdempotencyKey);
        Assert.Equal(250 + paid.AmountPaid, (await ReloadAsync(id)).Coins);
        await AssertReconciledAsync(id);
    }

    // ===== Kits =====

    [MySqlFact]
    public async Task KitPurchaseRacingPresenceAndStaleFullRowWrites_DeductsGemsExactlyOnce()
    {
        var id = await SignUpAsync("kit"); // 50 gems
        var kit = await AddKitAsync(k => { k.IsSinglePurchasePremium = true; k.PremiumPriceGems = 30; });

        // A copy loaded before the purchase, written back in full afterwards (audit A2 shape).
        await using var stale = _db.NewContext();
        var staleCopy = await stale.Users.AsNoTracking().SingleAsync(u => u.Id == id);

        var start = new TaskCompletionSource();
        var work = new List<Task>();
        for (var i = 0; i < 3; i++)
        {
            work.Add(Task.Run(async () =>
            {
                await start.Task;
                await using var r = NewRequest();
                try { await r.Kits.PurchaseKitAsync(id, kit.Id); }
                catch (InvalidOperationException) { /* already purchased */ }
                catch (DbUpdateException) { /* purchase unique index */ }
            }));
        }
        for (var i = 0; i < 10; i++)
        {
            var online = i % 2 == 0;
            work.Add(Task.Run(async () =>
            {
                await start.Task;
                await using var r = NewRequest();
                await r.UserRepo.UpdatePresenceAsync(id, online);
            }));
        }
        start.SetResult();
        await Task.WhenAll(work);

        stale.Users.Update(staleCopy);
        await stale.SaveChangesAsync();

        Assert.Equal(20, (await ReloadAsync(id)).Gems);
        var purchase = Assert.Single(await TransactionsForAsync(id), t => t.ReasonCode == CurrencyReasons.KitPurchase);
        Assert.Equal($"kit-purchase:{kit.Id}:{id}", purchase.IdempotencyKey);
        await using (var ctx = _db.NewContext())
        {
            Assert.Equal(1, await ctx.KitPurchases.CountAsync(p => p.UserId == id && p.KitId == kit.Id));
        }
        await AssertReconciledAsync(id);
    }

    [MySqlFact]
    public async Task KitClaimRetriedWithTheSameKey_PaysAndClaimsOnce()
    {
        var id = await SignUpAsync("claim");
        var kit = await AddKitAsync(k => { k.CostAmount = 100; k.CostCurrency = KitCostCurrency.Coins; });

        var start = new TaskCompletionSource();
        var claims = Enumerable.Range(0, 4).Select(async _ =>
        {
            await start.Task;
            await using var r = NewRequest();
            await r.Kits.ClaimKitAsync(id, kit.Id, PluginCall("claim-retry-1"));
        }).ToList();
        start.SetResult();
        await Task.WhenAll(claims);

        Assert.Equal(150, (await ReloadAsync(id)).Coins);
        Assert.Single(await TransactionsForAsync(id), t => t.ReasonCode == CurrencyReasons.KitClaimCost);
        await using (var ctx = _db.NewContext())
        {
            Assert.Equal(1, await ctx.KitClaims.CountAsync(c => c.UserId == id && c.KitId == kit.Id));
        }
    }

    // ===== Staff adjustments and title bonuses =====

    [MySqlFact]
    public async Task StaffSet_WithAStaleExpectedBalance_IsRefusedAndChangesNothing()
    {
        var id = await SignUpAsync("set");

        await using var r = NewRequest();
        var ex = await Assert.ThrowsAsync<CurrencyException>(() =>
            r.Users.AdjustBalancesAsync(id, Change(Currency.Coins, CurrencyOperation.Set, 1_000, expected: 999), Staff("stale-set")));

        Assert.Equal(CurrencyErrorCode.ExpectedBalanceMismatch, ex.Code);
        Assert.Equal(250, (await ReloadAsync(id)).Coins);
    }

    [MySqlFact]
    public async Task DemoteAndPromoteAgain_PaysTheTitleBonusOnce()
    {
        var id = await SignUpAsync("title");

        BalanceAdjustmentResultDto first, demoted, again;
        await using (var r = NewRequest())
        {
            first = await r.Users.AdjustBalancesAsync(id, Change(Currency.Experience, CurrencyOperation.Add, 2_500), Staff("xp-up-1"));
        }
        await using (var r = NewRequest())
        {
            demoted = await r.Users.AdjustBalancesAsync(id, Change(Currency.Experience, CurrencyOperation.Set, 0), Staff("xp-down"));
        }
        await using (var r = NewRequest())
        {
            again = await r.Users.AdjustBalancesAsync(id, Change(Currency.Experience, CurrencyOperation.Add, 2_500), Staff("xp-up-2"));
        }

        Assert.Equal((13_500, 3, 32), (first.TitleChange!.CoinBonusGranted, first.TitleChange.GemBonusGranted, first.TitleChange.ExpBonusGranted));
        Assert.Equal("demotion", demoted.TitleChange!.Direction);
        Assert.Equal("promotion", again.TitleChange!.Direction);
        Assert.Equal((0, 0, 0), (again.TitleChange.CoinBonusGranted, again.TitleChange.GemBonusGranted, again.TitleChange.ExpBonusGranted));

        var user = await ReloadAsync(id);
        Assert.Equal((250 + 13_500, 50 + 3, 2_500), (user.Coins, user.Gems, user.ExperiencePoints));
        var bonus = Assert.Single(await TransactionsForAsync(id), t => t.ReasonCode == CurrencyReasons.TitleBonus);
        Assert.Equal($"title-bonus:{id}:1", bonus.IdempotencyKey);
        await AssertReconciledAsync(id);
    }

    // ===== The acceptance script =====

    [MySqlFact]
    public async Task ScriptedSessionOverEveryPath_Reconciles()
    {
        // join (signup) → salary → staff set/add/remove + XP promotion → kit claim cost → kit
        // purchase → siege-shaped multi-leg reward + progression → account merge (forfeit).
        var main = await SignUpAsync("main");
        var alt = await SignUpAsync("alt");
        var ally = await SignUpAsync("ally");
        var costKit = await AddKitAsync(k => { k.CostAmount = 40; k.CostCurrency = KitCostCurrency.Coins; });
        var premiumKit = await AddKitAsync(k => { k.IsSinglePurchasePremium = true; k.PremiumPriceGems = 25; });

        await using (var ctx = _db.NewContext())
        {
            await ctx.Database.ExecuteSqlInterpolatedAsync($"UPDATE users SET LastSalaryPayoutAt = {DateTime.UtcNow.AddHours(-2)} WHERE Id = {main}");
        }
        await using (var r = NewRequest())
        {
            Assert.True((await r.Salary.PayOutAsync(main)).Paid);
        }
        await using (var r = NewRequest())
        {
            var set = await r.Users.AdjustBalancesAsync(main, Change(Currency.Coins, CurrencyOperation.Set, 5_000), Staff("s1"));
            Assert.Equal(5_000, set.NewCoins);
            // A retry of the same action is a replay, not a second posting.
            Assert.True((await r.Users.AdjustBalancesAsync(main, Change(Currency.Coins, CurrencyOperation.Set, 5_000), Staff("s1"))).Replayed);
        }
        await using (var r = NewRequest())
        {
            await r.Users.AdjustBalancesAsync(main, Change(Currency.Gems, CurrencyOperation.Add, 20), Staff("s2"));
            await r.Users.AdjustBalancesAsync(main, Change(Currency.Coins, CurrencyOperation.Remove, 1_000), Staff("s3"));
            var promoted = await r.Users.AdjustBalancesAsync(main, Change(Currency.Experience, CurrencyOperation.Add, 3_200), Staff("s4"));
            Assert.Equal(2, promoted.TitleChange!.CrossedTitles.Count); // Peasant, Yeoman
        }
        await using (var r = NewRequest())
        {
            await r.Kits.ClaimKitAsync(main, costKit.Id, PluginCall("claim-s5"));
        }
        await using (var r = NewRequest())
        {
            await r.Kits.PurchaseKitAsync(main, premiumKit.Id);
        }
        await using (var r = NewRequest())
        {
            // What SiegeMatchService.CompleteAsync does after the siege merge: one multi-leg
            // SIEGE_REWARD posting keyed by the match, then title progression for the XP legs,
            // inside the match's transaction.
            await r.UserRepo.RunWithUsersLockedAsync(new[] { main, ally }, async () =>
            {
                var posting = await r.Currency.PostAsync(new[]
                {
                    new CurrencyLeg(main, Currency.Coins, 300), new CurrencyLeg(main, Currency.Experience, 900),
                    new CurrencyLeg(ally, Currency.Coins, 150), new CurrencyLeg(ally, Currency.Gems, 2), new CurrencyLeg(ally, Currency.Experience, 2_600)
                }, CurrencyContext.ForSystem("SiegeMatchService", CurrencyReasons.SiegeReward, "siege-match:scripted-1"));
                var changes = await r.Titles.ApplyForPostingAsync(posting, null);
                Assert.True(changes.ContainsKey(ally)); // Serf → Peasant
            });
        }
        await using (var r = NewRequest())
        {
            await r.Users.MergeAccountsAsync(main, alt);
        }

        var alts = await ReloadAsync(alt);
        Assert.Equal((0, 0), (alts.Coins, alts.Gems));
        Assert.Single(await TransactionsForAsync(alt), t => t.ReasonCode == CurrencyReasons.MergeForfeit);
        var reasons = (await TransactionsForAsync(main)).Select(t => t.ReasonCode).Distinct().ToHashSet();
        Assert.Superset(new HashSet<string>
        {
            CurrencyReasons.SignupGrant, CurrencyReasons.Salary, CurrencyReasons.AdminSet, CurrencyReasons.AdminGrant,
            CurrencyReasons.AdminTake, CurrencyReasons.TitleBonus, CurrencyReasons.KitClaimCost, CurrencyReasons.KitPurchase,
            CurrencyReasons.SiegeReward
        }, reasons);
        await AssertReconciledAsync(main, alt, ally);
    }
}
