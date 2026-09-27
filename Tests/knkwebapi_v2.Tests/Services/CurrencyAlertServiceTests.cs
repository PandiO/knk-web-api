using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using static knkwebapi_v2.Tests.Services.CurrencyLedgerSeed;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Alert storage and the monitor loop (currency-payments IMPLEMENTATION_PLAN.md Phase 5):
/// suppression of repeated findings, R1 switching transfers off (and only once per mismatch set),
/// in-game notices for staff, R8/R9 signals becoming alerts, list/acknowledge, the reconciliation
/// run and its single-run gate, and a monitor cycle that survives an empty database and a
/// broken rule.
/// </summary>
public class CurrencyAlertServiceTests
{
    private readonly string _db = $"currency-alerts-{Guid.NewGuid()}";
    private readonly CurrencyReconciliationState _state = new();
    private readonly CurrencyMonitorSignals _signals = new();
    private readonly InMemoryPlayerNotificationQueue _queue = new();
    private readonly CurrencyMonitorOptions _options = new();

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_db).Options);

    private CurrencyAlertService Service(KnKDbContext ctx) =>
        new(ctx, new CurrencyReconciler(ctx), new CurrencyAnomalyDetector(ctx, Options.Create(_options)), _state, _signals,
            NullLogger<CurrencyAlertService>.Instance, Options.Create(_options), metrics: null, notifications: _queue);

    private async Task SeedPoliciesAsync()
    {
        await using var ctx = NewContext();
        ctx.CurrencyPolicies.Add(new CurrencyPolicy { Currency = Currency.Coins, Transferable = true, TransfersEnabled = true });
        ctx.CurrencyPolicies.Add(new CurrencyPolicy { Currency = Currency.Gems, TransfersEnabled = true });
        await ctx.SaveChangesAsync();
    }

    private static CurrencyAlertDraft Draft(string key = "R3:user:1", TimeSpan? suppress = null) =>
        new(CurrencyAlertRules.Funnel, CurrencyAlertSeverity.High, "Funnel into player1", key, suppress ?? TimeSpan.FromHours(24))
        {
            UserId = 1,
            Details = new { senders = 5 }
        };

    // ===== Raising =====

    [Fact]
    public async Task Raise_StoresTheAlert_AndSuppressesTheSameFindingWithinItsWindow()
    {
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User { Id = 1, Username = "alice" });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            var first = await Service(ctx).RaiseAsync(Draft());
            Assert.NotNull(first);
            Assert.Equal(("R3", "R3:user:1", 1), (first!.Rule, first.DedupKey, first.UserId));
            Assert.Equal(5, JsonDocument.Parse(first.DetailsJson!).RootElement.GetProperty("senders").GetInt32());

            Assert.Null(await Service(ctx).RaiseAsync(Draft()));
            Assert.NotNull(await Service(ctx).RaiseAsync(Draft("R3:user:2")));
        }

        // Outside the window the same finding is raised again.
        await using (var ctx = NewContext())
        {
            var stored = await ctx.CurrencyAlerts.Where(a => a.DedupKey == "R3:user:1").ToListAsync();
            stored.Single().CreatedAt = DateTime.UtcNow.AddHours(-25);
            await ctx.SaveChangesAsync();
            Assert.NotNull(await Service(ctx).RaiseAsync(Draft()));
        }

        // Online staff hear about each stored alert: a CurrencyAlert notice for nobody in particular.
        var notices = _queue.GetPending();
        Assert.Equal(3, notices.Count);
        Assert.All(notices, n => Assert.Equal((PlayerNotificationTypes.CurrencyAlert, 0, (string?)null), (n.Type, n.UserId, n.Uuid)));
        Assert.Equal(("R3", "Funnel", "High", "alice"), (notices[0].CurrencyAlert!.Rule, notices[0].CurrencyAlert!.RuleName,
            notices[0].CurrencyAlert!.Severity, notices[0].CurrencyAlert!.Username));
    }

    [Fact]
    public async Task Raise_BelowTheInGameSeverity_IsStoredButNotSentInGame()
    {
        _options.InGameMinSeverity = "High";
        await using var ctx = NewContext();

        await Service(ctx).RaiseAsync(new CurrencyAlertDraft(CurrencyAlertRules.Probing, CurrencyAlertSeverity.Low, "probing", "R9:user:1", TimeSpan.FromHours(1)));

        Assert.Equal(1, await ctx.CurrencyAlerts.CountAsync());
        Assert.Empty(_queue.GetPending());
    }

    [Fact]
    public async Task R1_SwitchesTransfersOffForTheMismatchedCurrency_Once()
    {
        await SeedPoliciesAsync();
        await using var ctx = NewContext();
        var r1 = new CurrencyAlertDraft(CurrencyAlertRules.Reconciliation, CurrencyAlertSeverity.Critical, "mismatch", "R1:abc", TimeSpan.FromHours(24))
        {
            DisableTransfersFor = new[] { Currency.Coins },
            Details = new { total = 1 }
        };

        var alert = await Service(ctx).RaiseAsync(r1);

        var policies = await ctx.CurrencyPolicies.AsNoTracking().ToDictionaryAsync(p => p.Currency);
        Assert.False(policies[Currency.Coins].TransfersEnabled);
        Assert.Null(policies[Currency.Coins].UpdatedByUserId);
        Assert.True(policies[Currency.Gems].TransfersEnabled);
        var details = JsonDocument.Parse(alert!.DetailsJson!).RootElement;
        Assert.Equal("Coins", details.GetProperty("transfersDisabled")[0].GetString());
        Assert.Equal(new[] { "Coins" }, _queue.GetPending().Single().CurrencyAlert!.TransfersDisabled);

        // Staff turn transfers back on; the same (known) mismatch set doesn't flip them again.
        var coins = await ctx.CurrencyPolicies.SingleAsync(p => p.Currency == Currency.Coins);
        coins.TransfersEnabled = true;
        await ctx.SaveChangesAsync();
        Assert.Null(await Service(ctx).RaiseAsync(r1));
        Assert.True((await ctx.CurrencyPolicies.AsNoTracking().SingleAsync(p => p.Currency == Currency.Coins)).TransfersEnabled);
    }

    [Fact]
    public async Task R1_KillSwitchCanBeTurnedOffInSettings()
    {
        _options.AutoDisableTransfersOnMismatch = false;
        await SeedPoliciesAsync();
        await using var ctx = NewContext();

        await Service(ctx).RaiseAsync(new CurrencyAlertDraft(CurrencyAlertRules.Reconciliation, CurrencyAlertSeverity.Critical, "m", "R1:x", TimeSpan.FromHours(24))
        {
            DisableTransfersFor = new[] { Currency.Coins }
        });

        Assert.True((await ctx.CurrencyPolicies.AsNoTracking().SingleAsync(p => p.Currency == Currency.Coins)).TransfersEnabled);
    }

    // ===== Reconciliation =====

    [Fact]
    public async Task Reconciliation_ReportsAMismatch_RaisesR1AndDisablesTransfers_ButChangesNoBalance()
    {
        await SeedPoliciesAsync();
        await using (var ctx = NewContext())
        {
            // The users column says 100, the ledger's last BalanceAfter says 50: a write outside the ledger.
            ctx.Users.Add(new User { Id = 1, Username = "alice", Coins = 100 });
            ctx.CurrencyTransactions.Add(new CurrencyTransaction
            {
                PublicId = CurrencyIds.NewPublicId(), Kind = CurrencyTransactionKind.Grant, ReasonCode = CurrencyReasons.EventReward, Reason = "x",
                IdempotencyScope = "system", IdempotencyKey = "event:1:1", RequestHash = new string('0', 64), Initiator = CurrencyInitiator.System,
                InitiatorComponent = "Test", CreatedAt = DateTime.UtcNow,
                Entries =
                {
                    new CurrencyEntry { Currency = Currency.Coins, AccountKind = CurrencyAccountKind.User, UserId = 1, Amount = 50, BalanceBefore = 0, BalanceAfter = 50 },
                    new CurrencyEntry { Currency = Currency.Coins, AccountKind = CurrencyAccountKind.System, SystemAccount = "SYS_EVENT", Amount = -50 }
                }
            });
            await ctx.SaveChangesAsync();
        }

        CurrencyReconciliationRunDto? run;
        await using (var ctx = NewContext())
        {
            run = await Service(ctx).RunReconciliationAsync("manual", 900);
        }

        Assert.NotNull(run);
        Assert.Equal((1, "manual", 900, (string?)null), (run!.MismatchCount, run.Trigger, run.TriggeredByUserId, run.Error));
        Assert.Equal("BalanceColumn", run.Mismatches.Single().Kind);
        Assert.Single(run.AlertIds);
        Assert.Equal(new[] { "Coins" }, run.TransfersDisabled);
        await using (var ctx = NewContext())
        {
            Assert.Equal(100, (await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == 1)).Coins);
            var alert = await ctx.CurrencyAlerts.SingleAsync();
            Assert.Equal((CurrencyAlertRules.Reconciliation, CurrencyAlertSeverity.Critical, 1), (alert.Rule, alert.Severity, alert.UserId));
            Assert.False((await ctx.CurrencyPolicies.SingleAsync(p => p.Currency == Currency.Coins)).TransfersEnabled);

            var status = Service(ctx).GetReconciliationStatus();
            Assert.Same(run, status.LastRun);
            Assert.False(status.Running);

            // The next run finds the same mismatch: reported, not alerted again.
            var again = await Service(ctx).RunReconciliationAsync("scheduled", null);
            Assert.Equal(1, again!.MismatchCount);
            Assert.Empty(again.AlertIds);
        }
    }

    [Fact]
    public async Task Reconciliation_OnlyOneRunAtATime()
    {
        await using var ctx = NewContext();
        Assert.True(_state.TryBegin());

        Assert.Null(await Service(ctx).RunReconciliationAsync("manual", 1));
        Assert.True(Service(ctx).GetReconciliationStatus().Running);

        _state.Complete(new CurrencyReconciliationRunDto { Trigger = "test" });
        var run = await Service(ctx).RunReconciliationAsync("manual", 1);
        Assert.Equal((0, (string?)null), (run!.MismatchCount, run.Error));
    }

    // ===== Signals (R8, R9) =====

    [Fact]
    public async Task FlushSignals_TurnsCapHitsAndProbingIntoAlerts_Once()
    {
        _signals.RecordCapHit(7, Currency.Coins, CurrencyReasons.Salary);
        _signals.RecordCapHit(7, Currency.Coins, CurrencyReasons.Salary);
        for (var i = 0; i < 21; i++)
        {
            _signals.RecordTransferDenial(8, CurrencyErrorCode.CooldownActive);
        }

        await using var ctx = NewContext();
        Assert.Equal(2, await Service(ctx).FlushSignalsAsync());
        Assert.Equal(0, await Service(ctx).FlushSignalsAsync());

        var alerts = await ctx.CurrencyAlerts.OrderBy(a => a.Rule).ToListAsync();
        Assert.Equal(("R8", CurrencyAlertSeverity.Medium, 7), (alerts[0].Rule, alerts[0].Severity, alerts[0].UserId));
        Assert.Equal(("R9", CurrencyAlertSeverity.Low, 8), (alerts[1].Rule, alerts[1].Severity, alerts[1].UserId));
    }

    // ===== Staff reads =====

    [Fact]
    public async Task List_FiltersByStatusSeverityAndRule_AndAcknowledgeIsRepeatable()
    {
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User { Id = 1, Username = "alice" });
            ctx.Users.Add(new User { Id = 900, Username = "moderator" });
            await ctx.SaveChangesAsync();
            var service = Service(ctx);
            await service.RaiseAsync(Draft("R3:user:1"));
            await service.RaiseAsync(new CurrencyAlertDraft(CurrencyAlertRules.Probing, CurrencyAlertSeverity.Low, "p", "R9:user:1", TimeSpan.FromHours(1)) { UserId = 1 });
            await service.RaiseAsync(new CurrencyAlertDraft(CurrencyAlertRules.Reconciliation, CurrencyAlertSeverity.Critical, "r", "R1:y", TimeSpan.FromHours(1)));
        }

        await using (var ctx = NewContext())
        {
            var service = Service(ctx);
            var open = await service.ListAsync(new CurrencyAlertQuery());
            Assert.Equal((3, 3), (open.TotalCount, open.OpenCount));
            Assert.Equal(new[] { "Critical", "High", "Low" }, open.OpenBySeverity.Keys);
            Assert.Equal("alice", open.Items.Single(a => a.Rule == "R3").Username);
            Assert.Equal("Funnel", open.Items.Single(a => a.Rule == "R3").RuleName);
            Assert.Equal(5, open.Items.Single(a => a.Rule == "R3").Details!.Value.GetProperty("senders").GetInt32());

            var highAndUp = await service.ListAsync(new CurrencyAlertQuery { MinSeverity = CurrencyAlertSeverity.High });
            Assert.Equal(new[] { "R1", "R3" }, highAndUp.Items.Select(a => a.Rule).OrderBy(r => r));
            Assert.Equal("R9", (await service.ListAsync(new CurrencyAlertQuery { Rule = "r9" })).Items.Single().Rule);

            var id = open.Items.Single(a => a.Rule == "R3").Id;
            var acked = await service.AcknowledgeAsync(id, 900);
            Assert.Equal((900, "moderator"), (acked.AckedByUserId, acked.AckedByUsername));
            var ackedAt = acked.AckedAt;
            Assert.Equal(ackedAt, (await service.AcknowledgeAsync(id, 901)).AckedAt);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.AcknowledgeAsync(12345, 900));

            Assert.Equal(2, (await service.ListAsync(new CurrencyAlertQuery())).OpenCount);
            Assert.Equal(id, (await service.ListAsync(new CurrencyAlertQuery { Status = "acked" })).Items.Single().Id);
            Assert.Equal(3, (await service.ListAsync(new CurrencyAlertQuery { Status = "all" })).TotalCount);
        }
    }

    // ===== The monitor loop =====

    private ServiceProvider Provider(Action<IServiceCollection>? replace = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase(_db));
        services.AddSingleton(Options.Create(_options));
        services.AddSingleton(_state);
        services.AddSingleton(_signals);
        services.AddSingleton<IPlayerNotificationQueue>(_queue);
        services.AddSingleton<CurrencyMetrics>();
        services.AddScoped<CurrencyReconciler>();
        services.AddScoped<CurrencyAnomalyDetector>();
        services.AddScoped<ICurrencyAlertService, CurrencyAlertService>();
        replace?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task MonitorCycle_SurvivesAnEmptyDatabase_AndRunsEachStepWhenDue()
    {
        await using var provider = Provider();
        var monitor = new CurrencyMonitorService(provider, NullLogger<CurrencyMonitorService>.Instance, Options.Create(_options));
        var now = DateTime.UtcNow;

        await monitor.RunCycleAsync(now);

        Assert.Equal((0, "scheduled"), (_state.LastRun!.MismatchCount, _state.LastRun.Trigger));
        var first = _state.LastRun;

        // A minute later: signals only (the rules wait 5 min, the reconciliation an hour).
        _signals.RecordCapHit(3, Currency.Gems, CurrencyReasons.AdminGrant);
        await monitor.RunCycleAsync(now.AddMinutes(1));
        Assert.Same(first, _state.LastRun);
        await using (var ctx = NewContext())
        {
            Assert.Equal("R8", (await ctx.CurrencyAlerts.SingleAsync()).Rule);
        }

        await monitor.RunCycleAsync(now.AddMinutes(61));
        Assert.NotSame(first, _state.LastRun);
    }

    [Fact]
    public async Task MonitorCycle_KeepsGoingWhenAStepThrows()
    {
        await using var provider = Provider(services =>
            services.AddScoped<ICurrencyAlertService>(_ => throw new InvalidOperationException("database down")));
        var monitor = new CurrencyMonitorService(provider, NullLogger<CurrencyMonitorService>.Instance, Options.Create(_options));

        await monitor.RunCycleAsync(DateTime.UtcNow);
        await monitor.RunCycleAsync(DateTime.UtcNow.AddHours(2));
    }

    [Fact]
    public async Task Rules_RaiseAlertsFromTheLedger_OncePerWindow()
    {
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User { Id = 1, Username = "alice", CreatedAt = DateTime.UtcNow.AddDays(-100) });
            await ctx.SaveChangesAsync();
            await AddAsync(ctx, Tx(CurrencyTransactionKind.Grant, CurrencyReasons.EventReward, DateTime.UtcNow.AddMinutes(-5), (1, Currency.Coins, 900_000)));
        }

        await using (var ctx = NewContext())
        {
            Assert.Equal(1, await Service(ctx).RunRulesAsync(DateTime.UtcNow));
            Assert.Equal(0, await Service(ctx).RunRulesAsync(DateTime.UtcNow.AddMinutes(5)));
            Assert.Equal(CurrencyAlertRules.Velocity, (await ctx.CurrencyAlerts.SingleAsync()).Rule);
        }
    }
}
