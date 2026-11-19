using System;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Services.Territory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Services.Stock
{
    /// <summary>What one maintenance tick did.</summary>
    public sealed record StockMaintenanceOutcome(bool ProductionEnabled, StockReconciliationReport? Reconciliation,
        StockRollupResult? Rollup);

    /// <summary>
    /// The stock core's background work (G16): the scheduled reconciler run and the daily roll-up. Both run only while
    /// the game setting <c>production.enabled</c> (<c>Production:Enabled</c>) is on; with it off a tick does nothing.
    /// Admins can still run both by hand (<c>api/StockTransactions/reconcile</c>, <c>…/rollup</c>, DESIGN § 18.2).
    /// </summary>
    public sealed class StockMaintenanceJob
    {
        private readonly IProductionSettingsReader _settings;
        private readonly IStockReconciler _reconciler;
        private readonly IStockRollupService _rollup;

        public StockMaintenanceJob(IProductionSettingsReader settings, IStockReconciler reconciler, IStockRollupService rollup)
        {
            _settings = settings;
            _reconciler = reconciler;
            _rollup = rollup;
        }

        public async Task<StockMaintenanceOutcome> RunAsync(bool reconcile, bool rollUp, CancellationToken ct = default)
        {
            if (!(await _settings.GetAsync()).Enabled) return new StockMaintenanceOutcome(false, null, null);
            var report = reconcile ? await _reconciler.RunAsync(ct) : null;
            var rolled = rollUp ? await _rollup.RollUpAsync(ct) : null;
            return new StockMaintenanceOutcome(true, report, rolled);
        }
    }

    /// <summary>
    /// Hourly tick: reconcile every <see cref="ReconcileEvery"/>, roll up every <see cref="RollUpEvery"/>, both only
    /// with <c>Production:Enabled</c> on (<see cref="StockMaintenanceJob"/>). A difference is logged as a warning.
    /// </summary>
    public sealed class StockMaintenanceService : BackgroundService
    {
        public static readonly TimeSpan Tick = TimeSpan.FromHours(1);
        public static readonly TimeSpan ReconcileEvery = TimeSpan.FromHours(6);
        public static readonly TimeSpan RollUpEvery = TimeSpan.FromHours(24);

        private readonly IServiceProvider _services;
        private readonly ILogger<StockMaintenanceService> _logger;
        private readonly ConnectivityAwareFailureLogger _failures;

        public StockMaintenanceService(IServiceProvider services, ILogger<StockMaintenanceService> logger)
        {
            _services = services;
            _logger = logger;
            _failures = new ConnectivityAwareFailureLogger(logger, "Stock maintenance");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            DateTime? lastReconcile = null, lastRollUp = null;
            using var timer = new PeriodicTimer(Tick);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    var now = DateTime.UtcNow;
                    bool reconcile = lastReconcile == null || now - lastReconcile >= ReconcileEvery;
                    bool rollUp = lastRollUp == null || now - lastRollUp >= RollUpEvery;
                    if (!reconcile && !rollUp) continue;
                    try
                    {
                        using var scope = _services.CreateScope();
                        var outcome = await scope.ServiceProvider.GetRequiredService<StockMaintenanceJob>()
                            .RunAsync(reconcile, rollUp, stoppingToken);
                        if (outcome.ProductionEnabled)
                        {
                            if (reconcile) lastReconcile = now;
                            if (rollUp) lastRollUp = now;
                            if (outcome.Reconciliation is { Clean: false } r)
                                _logger.LogWarning(
                                    "Stock reconciler: {Imbalances} blueprint imbalance(s), {Lines} line difference(s), {Units} unit difference(s), {Totals} total difference(s)",
                                    r.BlueprintImbalanceCount, r.LineDifferenceCount, r.UnitDifferenceCount, r.TotalDifferenceCount);
                            if (outcome.Rollup is { TransactionsRolledUp: > 0 } u)
                                _logger.LogInformation("Stock roll-up: {Transactions} transaction(s), {Moves} move(s) into {Rows} row(s)",
                                    u.TransactionsRolledUp, u.MovesRolledUp, u.RollupRowsWritten);
                        }
                        _failures.Succeeded();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _failures.Failed(ex, "Stock maintenance failed");
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }
}
