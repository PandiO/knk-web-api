using knkwebapi_v2.Configuration;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// The currency monitor (docs/specs/currency-payments/DESIGN.md §3.9, IMPLEMENTATION_PLAN.md
    /// Phase 5), registered like RetentionPolicyService. Every CycleSeconds (default 60 s) it
    /// stores the R8/R9 findings collected in memory; every RuleIntervalMinutes (5) it runs the
    /// ledger rules R3–R7; every ReconciliationIntervalMinutes (60) the reconciler (R1/R2), which
    /// also runs once shortly after startup. Each step uses its own DI scope, and a failing step
    /// is logged and retried next time — it never stops the loop.
    /// <para>
    /// Report only: it never corrects a balance or a ledger row. The one automatic action is R1's
    /// kill switch for player transfers (DESIGN.md §4 D8), which staff reverse in the currency policy.
    /// </para>
    /// </summary>
    public class CurrencyMonitorService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CurrencyMonitorService> _logger;
        private readonly CurrencyMonitorOptions _options;
        private DateTime _nextRules = DateTime.MinValue;
        private DateTime _nextReconciliation = DateTime.MinValue;

        public CurrencyMonitorService(IServiceProvider serviceProvider, ILogger<CurrencyMonitorService> logger,
            IOptions<CurrencyMonitorOptions>? options = null)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _options = options?.Value ?? new CurrencyMonitorOptions();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("Currency monitor disabled (CurrencyMonitor:Enabled = false)");
                return;
            }
            _logger.LogInformation("Currency monitor started: cycle {Cycle}s, rules every {Rules} min, reconciliation every {Reconcile} min",
                _options.CycleSeconds, _options.RuleIntervalMinutes, _options.ReconciliationIntervalMinutes);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, _options.StartupDelaySeconds)), stoppingToken);
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _options.CycleSeconds)));
                do
                {
                    await RunCycleAsync(DateTime.UtcNow, stoppingToken);
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Currency monitor stopping");
            }
        }

        /// <summary>
        /// One cycle at <paramref name="now"/>: signals always; the rules and the reconciliation
        /// when due. Public so tests can drive the monitor without timers.
        /// </summary>
        public async Task RunCycleAsync(DateTime now, CancellationToken ct = default)
        {
            await StepAsync("signals", (alerts, token) => alerts.FlushSignalsAsync(token), ct);

            if (now >= _nextRules)
            {
                _nextRules = now.AddMinutes(Math.Max(1, _options.RuleIntervalMinutes));
                await StepAsync("rules", (alerts, token) => alerts.RunRulesAsync(now, token), ct);
            }
            if (now >= _nextReconciliation)
            {
                _nextReconciliation = now.AddMinutes(Math.Max(1, _options.ReconciliationIntervalMinutes));
                await StepAsync("reconciliation", async (alerts, token) =>
                {
                    var run = await alerts.RunReconciliationAsync("scheduled", null, token);
                    return run?.AlertIds.Count ?? 0;
                }, ct);
            }
        }

        private async Task StepAsync(string step, Func<ICurrencyAlertService, CancellationToken, Task<int>> work, CancellationToken ct)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var alerts = scope.ServiceProvider.GetRequiredService<ICurrencyAlertService>();
                var raised = await work(alerts, ct);
                if (raised > 0)
                {
                    _logger.LogInformation("Currency monitor {Step}: {Count} alert(s) raised", step, raised);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Currency monitor step {Step} failed", step);
            }
        }
    }
}
