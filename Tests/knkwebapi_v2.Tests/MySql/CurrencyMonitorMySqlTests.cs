using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using static knkwebapi_v2.Tests.Services.CurrencyLedgerSeed;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// The currency monitor against a real MySQL (currency-payments IMPLEMENTATION_PLAN.md Phase 5,
/// requires-mysql): the scheduled reconciliation job on a ledger written by the real
/// CurrencyService reports nothing, then reports a balance changed behind the ledger's back as
/// one R1 alert, switches coin transfers off (a transfer is then refused) and changes no balance;
/// the same mismatch isn't alerted twice.
/// </summary>
[Trait("Category", "requires-mysql")]
public class CurrencyMonitorMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;
    private readonly CurrencyReconciliationState _state = new();

    public CurrencyMonitorMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static CurrencyService Currency(KnKDbContext ctx) =>
        new(new CurrencyRepository(ctx), new UserRepository(ctx), NullLogger<CurrencyService>.Instance);

    private CurrencyAlertService Alerts(KnKDbContext ctx) =>
        new(ctx, new CurrencyReconciler(ctx), new CurrencyAnomalyDetector(ctx), _state, new CurrencyMonitorSignals(),
            NullLogger<CurrencyAlertService>.Instance, Options.Create(new CurrencyMonitorOptions()));

    private async Task<int> SeedUserAsync()
    {
        await using var ctx = _db.NewContext();
        var user = new User { Username = "m" + Guid.NewGuid().ToString("N")[..12], CreatedAt = DateTime.UtcNow.AddDays(-30) };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    [MySqlFact]
    public async Task ScheduledReconciliation_ReportsATamperedBalance_OnceAndSwitchesTransfersOff()
    {
        var alice = await SeedUserAsync();
        var bob = await SeedUserAsync();
        await using (var ctx = _db.NewContext())
        {
            await Currency(ctx).GrantAsync(alice, Enums.Currency.Coins, 5_000, CurrencyContext.ForSystem("MySqlTest", CurrencyReasons.EventReward, $"event:m:{alice}"));
            await Currency(ctx).GrantAsync(bob, Enums.Currency.Gems, 30, CurrencyContext.ForSystem("MySqlTest", CurrencyReasons.EventReward, $"event:m:{bob}"));
        }

        await using (var ctx = _db.NewContext())
        {
            var clean = await Alerts(ctx).RunReconciliationAsync("scheduled", null);
            Assert.Equal((0, (string?)null), (clean!.MismatchCount, clean.Error));
            Assert.Empty(clean.AlertIds);
        }

        // A manual SQL edit behind the ledger's back.
        await using (var ctx = _db.NewContext())
        {
            await ctx.Database.ExecuteSqlRawAsync($"UPDATE users SET Coins = Coins + 777 WHERE Id = {alice}");
        }

        await using (var ctx = _db.NewContext())
        {
            var run = await Alerts(ctx).RunReconciliationAsync("scheduled", null);
            Assert.Null(run!.Error);
            var mismatch = Assert.Single(run.Mismatches);
            Assert.Equal(("BalanceColumn", alice, 5_000L, 5_777L), (mismatch.Kind, mismatch.UserId, mismatch.Expected, mismatch.Actual));
            Assert.Single(run.AlertIds);
            Assert.Equal(new[] { "Coins" }, run.TransfersDisabled);
        }

        await using (var ctx = _db.NewContext())
        {
            var alert = await ctx.CurrencyAlerts.AsNoTracking().SingleAsync();
            Assert.Equal((CurrencyAlertRules.Reconciliation, CurrencyAlertSeverity.Critical, alice), (alert.Rule, alert.Severity, alert.UserId));
            Assert.Contains("\"transfersDisabled\"", alert.DetailsJson);
            var policies = await ctx.CurrencyPolicies.AsNoTracking().ToDictionaryAsync(p => p.Currency);
            Assert.False(policies[Enums.Currency.Coins].TransfersEnabled);
            Assert.True(policies[Enums.Currency.Gems].TransfersEnabled);
            // Report only: the tampered column is left as found.
            Assert.Equal(5_777, (await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == alice)).Coins);

            // The kill switch stops player transfers of coins.
            var ex = await Assert.ThrowsAsync<CurrencyException>(() => Currency(ctx).TransferAsync(
                new TransferRequest(alice, bob, Enums.Currency.Coins, 100),
                CurrencyContext.ForCaller(new KnkCaller(true, false, null, alice), CurrencyReasons.PlayerTransfer, "pay-m-1", "MySqlTest", staffAction: false)));
            Assert.Equal(CurrencyErrorCode.TransfersDisabled, ex.Code);
        }

        // The next hourly run still reports it, but doesn't alert (or flip the switch) again.
        await using (var ctx = _db.NewContext())
        {
            var coins = await ctx.CurrencyPolicies.SingleAsync(p => p.Currency == Enums.Currency.Coins);
            coins.TransfersEnabled = true;
            await ctx.SaveChangesAsync();

            var again = await Alerts(ctx).RunReconciliationAsync("scheduled", null);
            Assert.Equal(1, again!.MismatchCount);
            Assert.Empty(again.AlertIds);
            Assert.Equal(1, await ctx.CurrencyAlerts.CountAsync());
            Assert.True((await ctx.CurrencyPolicies.AsNoTracking().SingleAsync(p => p.Currency == Enums.Currency.Coins)).TransfersEnabled);
        }
    }
}

/// <summary>
/// Every ledger rule (R3–R7) translates to MySQL and fires on a seeded ledger, alerts are stored
/// once (the dedup query and the json details column), and a full monitor cycle — signals,
/// rules, reconciliation — runs against MySQL through DI. Separate database from
/// <see cref="CurrencyMonitorMySqlTests"/>: the synthetic ledger here doesn't chain.
/// </summary>
[Trait("Category", "requires-mysql")]
public class CurrencyMonitorRulesMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public CurrencyMonitorRulesMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private async Task<List<int>> SeedUsersAsync(int count, DateTime createdAt)
    {
        await using var ctx = _db.NewContext();
        var users = Enumerable.Range(0, count)
            .Select(_ => new User { Username = "r" + Guid.NewGuid().ToString("N")[..12], CreatedAt = createdAt }).ToList();
        ctx.Users.AddRange(users);
        await ctx.SaveChangesAsync();
        return users.Select(u => u.Id).ToList();
    }

    private ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<KnKDbContext>(o => o.UseMySql(_db.ConnectionString, ServerVersion.AutoDetect(_db.ConnectionString)));
        services.AddSingleton(Options.Create(new CurrencyMonitorOptions()));
        services.AddSingleton<CurrencyReconciliationState>();
        services.AddSingleton<CurrencyMonitorSignals>();
        services.AddSingleton<CurrencyMetrics>();
        services.AddSingleton<IPlayerNotificationQueue, InMemoryPlayerNotificationQueue>();
        services.AddScoped<CurrencyReconciler>();
        services.AddScoped<CurrencyAnomalyDetector>();
        services.AddScoped<ICurrencyAlertService, CurrencyAlertService>();
        return services.BuildServiceProvider();
    }

    [MySqlFact]
    public async Task EveryRule_FiresOnASeededLedger_AndAlertsOnce_ThroughAFullMonitorCycle()
    {
        var now = DateTime.UtcNow;
        var receiver = (await SeedUsersAsync(1, now.AddDays(-400)))[0];
        var alts = await SeedUsersAsync(5, now.AddDays(-1));
        var (a, b) = (alts[0], alts[1]);
        var staff = (await SeedUsersAsync(1, now.AddDays(-400)))[0];

        await using (var ctx = _db.NewContext())
        {
            var seeded = new List<CurrencyTransaction>();
            // R3: five new accounts pay the receiver.
            seeded.AddRange(alts.Select((alt, i) => Transfer(alt, receiver, 1_000, now.AddHours(-2).AddMinutes(i))));
            // R4: a and b pass coins back and forth three times.
            for (var i = 0; i < 3; i++)
            {
                seeded.Add(Transfer(a, b, 50, now.AddMinutes(-40 + i * 10)));
                seeded.Add(Transfer(b, a, 50, now.AddMinutes(-35 + i * 10)));
            }
            // R5 + R6: a 2,000,000 coin staff grant to the receiver within the hour.
            seeded.Add(Tx(CurrencyTransactionKind.AdminAdjust, CurrencyReasons.AdminGrant, now.AddMinutes(-20), null, null, staff,
                (receiver, Enums.Currency.Coins, 2_000_000)));
            // R7: event rewards 10x their weekly mean today.
            seeded.AddRange(Enumerable.Range(1, 7).Select(day =>
                Tx(CurrencyTransactionKind.Grant, CurrencyReasons.EventReward, now.AddDays(-day).AddHours(-1), (a, Enums.Currency.Gems, 50))));
            seeded.Add(Tx(CurrencyTransactionKind.Grant, CurrencyReasons.EventReward, now.AddHours(-3), (a, Enums.Currency.Gems, 500)));
            await AddAsync(ctx, seeded.ToArray());
        }

        await using var provider = Provider();
        var monitor = new CurrencyMonitorService(provider, NullLogger<CurrencyMonitorService>.Instance,
            Options.Create(new CurrencyMonitorOptions()));
        provider.GetRequiredService<CurrencyMonitorSignals>().RecordCapHit(receiver, Enums.Currency.Coins, CurrencyReasons.Salary);

        await monitor.RunCycleAsync(DateTime.UtcNow);

        await using (var ctx = _db.NewContext())
        {
            var rules = (await ctx.CurrencyAlerts.AsNoTracking().Select(x => x.Rule).ToListAsync()).OrderBy(r => r).ToList();
            // R1: the synthetic ledger doesn't chain, so the reconciliation (run in the same cycle) reports it too.
            Assert.Equal(new[] { "R1", "R3", "R4", "R5", "R6", "R7", "R8" }, rules.Distinct());
            Assert.Equal(1, rules.Count(r => r == "R6"));
            Assert.Null(provider.GetRequiredService<CurrencyReconciliationState>().LastRun!.Error);
        }

        // Rules again a few minutes later: nothing new.
        await using (var scope = provider.CreateAsyncScope())
        {
            var raised = await scope.ServiceProvider.GetRequiredService<ICurrencyAlertService>().RunRulesAsync(DateTime.UtcNow.AddMinutes(5));
            Assert.Equal(0, raised);
        }
    }
}

/// <summary>
/// The reconciler reads the users columns and the ledger legs in separate queries. A posting
/// that commits between them must not look like a mismatch (a false R1 alert switches player
/// transfers off for everyone): all its reads come from one snapshot.
/// </summary>
[Trait("Category", "requires-mysql")]
public class CurrencyReconcilerSnapshotMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public CurrencyReconcilerSnapshotMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    /// <summary>Runs <see cref="Between"/> once, right after the reconciler's users-balance query.</summary>
    private sealed class AfterBalancesRead : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public Func<Task>? Between { get; set; }

        public override async ValueTask<System.Data.Common.DbDataReader> ReaderExecutedAsync(System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandExecutedEventData eventData, System.Data.Common.DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Between != null && command.CommandText.Contains("`users`") && command.CommandText.Contains("`ExperiencePoints`")
                && !command.CommandText.Contains("currency_entries"))
            {
                var between = Between;
                Between = null;
                await between();
            }
            return result;
        }
    }

    [MySqlFact]
    public async Task APostingCommittedMidRun_IsNotReportedAsAMismatch()
    {
        int id;
        await using (var ctx = _db.NewContext())
        {
            var user = new User { Username = "s" + Guid.NewGuid().ToString("N")[..12], CreatedAt = DateTime.UtcNow.AddDays(-30) };
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync();
            id = user.Id;
        }
        static CurrencyService Currency(KnKDbContext ctx) =>
            new(new CurrencyRepository(ctx), new UserRepository(ctx), NullLogger<CurrencyService>.Instance);
        await using (var ctx = _db.NewContext())
        {
            await Currency(ctx).GrantAsync(id, Enums.Currency.Coins, 1_000, CurrencyContext.ForSystem("MySqlTest", CurrencyReasons.EventReward, $"event:s1:{id}"));
        }

        var interceptor = new AfterBalancesRead
        {
            Between = async () =>
            {
                await using var other = _db.NewContext();
                await Currency(other).GrantAsync(id, Enums.Currency.Coins, 250, CurrencyContext.ForSystem("MySqlTest", CurrencyReasons.EventReward, $"event:s2:{id}"));
            }
        };
        var options = new DbContextOptionsBuilder<KnKDbContext>()
            .UseMySql(_db.ConnectionString, ServerVersion.AutoDetect(_db.ConnectionString))
            .AddInterceptors(interceptor)
            .Options;
        await using (var ctx = new KnKDbContext(options))
        {
            var mismatches = await new CurrencyReconciler(ctx).FindMismatchesAsync();
            Assert.Null(interceptor.Between); // the posting did land mid-run
            Assert.Empty(mismatches);
        }

        // Both postings are there, and a fresh run reconciles them.
        await using (var ctx = _db.NewContext())
        {
            Assert.Equal(1_250, (await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == id)).Coins);
            Assert.Empty(await new CurrencyReconciler(ctx).FindMismatchesAsync());
        }
    }
}
