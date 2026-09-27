using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// Staff tooling guarantees only a real MySQL can prove (currency-payments IMPLEMENTATION_PLAN.md
/// Phase 4 "requires-mysql"): racing reversals of one transaction post exactly one (whichever
/// caller or key), a reversal racing a spend of the same money never takes the balance below
/// zero, and one staff member's concurrent grants respect their daily cap exactly.
/// </summary>
[Trait("Category", "requires-mysql")]
public class CurrencyAdminMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public CurrencyAdminMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static CurrencyService Currency(KnKDbContext ctx) =>
        new(new CurrencyRepository(ctx), new UserRepository(ctx), NullLogger<CurrencyService>.Instance,
            new Mock<IPermissionResolutionService>().Object);

    private static CurrencyAdminService Admin(KnKDbContext ctx)
    {
        var users = new UserRepository(ctx);
        var titles = new TitleService(new TitleBracketRepository(ctx));
        var audit = new AuditLogService(new AuditLogRepository(ctx), users);
        var memberships = new UserPermissionGroupService(new UserPermissionGroupRepository(ctx), users, new PermissionGroupRepository(ctx), audit);
        var currency = Currency(ctx);
        return new CurrencyAdminService(currency, new CurrencyRepository(ctx), users, audit,
            new TitleProgressionService(currency, users, titles, memberships, audit));
    }

    private async Task<int> SeedUserAsync()
    {
        await using var ctx = _db.NewContext();
        var user = new User { Username = "a" + Guid.NewGuid().ToString("N")[..12], CreatedAt = DateTime.UtcNow.AddDays(-10) };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private async Task<User> ReloadAsync(int id)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    private async Task<PostingResult> GrantAsync(int userId, long amount)
    {
        await using var ctx = _db.NewContext();
        return await Currency(ctx).GrantAsync(userId, Enums.Currency.Coins, amount,
            CurrencyContext.ForSystem("MySqlTest", CurrencyReasons.EventReward, "event:" + Guid.NewGuid().ToString("N")));
    }

    private static async Task<List<Exception?>> InParallelAsync(int count, Func<int, Task> call)
    {
        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            await start.Task;
            try
            {
                await call(i);
                return (Exception?)null;
            }
            catch (Exception ex)
            {
                return ex;
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
    public async Task RacingReversals_FromWebAndGameServer_PostExactlyOne()
    {
        var staff = await SeedUserAsync();
        var player = await SeedUserAsync();
        var grant = await GrantAsync(player, 500);

        var errors = await InParallelAsync(12, async i =>
        {
            await using var ctx = _db.NewContext();
            var caller = i % 2 == 0
                ? new KnkCaller(isPluginService: false, isWebUser: true, webUserId: staff, actingUserId: null)
                : new KnkCaller(isPluginService: true, isWebUser: false, webUserId: null, actingUserId: staff);
            await Admin(ctx).ReverseAsync(grant.PublicId, new ReverseTransactionDto { Note = "Granted by mistake" }, caller, "MySqlTest");
        });

        // Exactly one succeeds; every other request - a same-key retry too (KNG-21) - is told it
        // was already reversed, with which reversal and by whom.
        Assert.Single(errors, e => e == null);
        await using var check = _db.NewContext();
        var reversal = await check.CurrencyTransactions.SingleAsync(t => t.ReversesTransactionId == grant.TransactionId);
        Assert.All(errors.Where(e => e != null), e =>
        {
            var ex = Assert.IsType<CurrencyException>(e);
            Assert.Equal(CurrencyErrorCode.AlreadyReversed, ex.Code);
            var details = Assert.IsType<AlreadyReversedDetailsDto>(ex.Details);
            Assert.Equal((reversal.PublicId, (int?)staff), (details.ReversalTransactionPublicId, details.ReversedByUserId));
            Assert.NotNull(details.ReversedByUsername);
        });
        Assert.Equal(1, await check.AuditLogEntries.CountAsync(a => a.TargetUserId == player && a.Action == AuditAction.CurrencyTransactionReversed));
        Assert.Equal(0, (await ReloadAsync(player)).Coins);
        await AssertReconciledAsync(player);
    }

    [MySqlFact]
    public async Task ReversalRacingASpend_NeverGoesBelowZero()
    {
        var staff = await SeedUserAsync();
        for (var round = 0; round < 5; round++)
        {
            var player = await SeedUserAsync();
            var grant = await GrantAsync(player, 100);

            var errors = await InParallelAsync(2, async i =>
            {
                await using var ctx = _db.NewContext();
                if (i == 0)
                {
                    await Currency(ctx).SpendAsync(player, Enums.Currency.Coins, 100,
                        CurrencyContext.ForSystem("MySqlTest", CurrencyReasons.KitPurchase, $"kit-purchase:race:{player}"));
                }
                else
                {
                    await Admin(ctx).ReverseAsync(grant.PublicId, new ReverseTransactionDto { Note = "Granted by mistake" },
                        new KnkCaller(false, true, staff, null), "MySqlTest");
                }
            });

            // Exactly one of them gets the 100 coins; the other is refused, and nothing goes negative.
            Assert.Single(errors, e => e == null);
            var refused = Assert.IsType<CurrencyException>(Assert.Single(errors, e => e != null));
            Assert.Contains(refused.Code, new[] { CurrencyErrorCode.InsufficientFunds, CurrencyErrorCode.ReversalWouldGoNegative });
            Assert.Equal(0, (await ReloadAsync(player)).Coins);
            await AssertReconciledAsync(player);
        }
    }

    [MySqlFact]
    public async Task OneStaffMembersConcurrentGrants_RespectTheDailyCapExactly()
    {
        var staff = await SeedUserAsync();
        var players = new[] { await SeedUserAsync(), await SeedUserAsync(), await SeedUserAsync() };
        await using (var ctx = _db.NewContext())
        {
            await ctx.Database.ExecuteSqlRawAsync("UPDATE currency_policies SET AdminDailyGrantCapPerActor = 1000 WHERE Currency = 0");
        }
        try
        {
            var errors = await InParallelAsync(12, async i =>
            {
                await using var ctx = _db.NewContext();
                await Currency(ctx).AdminAdjustAsync(new AdminAdjustRequest(players[i % 3], Enums.Currency.Coins, CurrencyOperation.Add, 200), new CurrencyContext
                {
                    IdempotencyKey = $"cap:{staff}:{i}",
                    IdempotencyScope = CurrencyIdempotencyScopes.Web,
                    ReasonCode = CurrencyReasons.AdminGrant,
                    Reason = "Event prizes",
                    Initiator = CurrencyInitiator.Admin,
                    InitiatorUserId = staff,
                    InitiatorComponent = "MySqlTest"
                });
            });

            Assert.Equal(5, errors.Count(e => e == null));
            Assert.All(errors.Where(e => e != null), e => Assert.Equal(CurrencyErrorCode.AdminDailyCapExceeded, Assert.IsType<CurrencyException>(e).Code));
            var total = 0;
            foreach (var p in players) total += (await ReloadAsync(p)).Coins;
            Assert.Equal(1000, total);
            await AssertReconciledAsync(players);
        }
        finally
        {
            await using var ctx = _db.NewContext();
            await ctx.Database.ExecuteSqlRawAsync("UPDATE currency_policies SET AdminDailyGrantCapPerActor = 5000000 WHERE Currency = 0");
        }
    }

    [MySqlFact]
    public async Task EventLog_EverySortAndFilter_TranslatesToSql()
    {
        var staff = await SeedUserAsync();
        var player = await SeedUserAsync();
        await GrantAsync(player, 300);
        await using var ctx = _db.NewContext();
        await Currency(ctx).AdminAdjustAsync(new AdminAdjustRequest(player, Enums.Currency.Coins, CurrencyOperation.Set, 50), new CurrencyContext
        {
            IdempotencyKey = "log:" + Guid.NewGuid().ToString("N"), IdempotencyScope = CurrencyIdempotencyScopes.Web,
            ReasonCode = CurrencyReasons.AdminSet, Reason = "Correction: duplicated prize", Initiator = CurrencyInitiator.Admin,
            InitiatorUserId = staff, InitiatorComponent = "MySqlTest"
        });
        var name = (await ReloadAsync(player)).Username;
        var staffName = (await ReloadAsync(staff)).Username;

        foreach (var sort in Enum.GetValues<LedgerSort>())
        {
            foreach (var descending in new[] { true, false })
            {
                var page = await Currency(ctx).GetHistoryAsync(new LedgerQuery { UserSearch = name[..8], Sort = sort, Descending = descending });
                Assert.Equal(2, page.TotalCount);
            }
        }
        var byStaff = await Currency(ctx).GetHistoryAsync(new LedgerQuery
        {
            InitiatorSearch = staffName[..8], Currency = Enums.Currency.Coins, Kind = CurrencyTransactionKind.AdminAdjust,
            Initiator = CurrencyInitiator.Admin, From = DateTime.UtcNow.AddMinutes(-5), To = DateTime.UtcNow.AddMinutes(5)
        });
        var line = Assert.Single(byStaff.Items);
        Assert.Equal((name, staffName, "Set", -250L, 300L, 50L), (line.Username, line.InitiatorUsername, line.Operation, line.Amount, line.BalanceBefore, line.BalanceAfter));
    }
}
