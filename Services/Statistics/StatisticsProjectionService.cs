using System;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Statistics
{
    /// <summary>
    /// Runs the statistics projectors every Statistics:ProjectionIntervalSeconds (default 30;
    /// IMPLEMENTATION_PLAN.md §4): the ledger projector and the Siege match projector until caught
    /// up (bounded per cycle), then closes sessions without a heartbeat for
    /// Statistics:SessionTimeoutMinutes. Each step has its own scope and try/catch, so one failing
    /// step never stops the others. Does nothing when Statistics:Enabled is false.
    /// </summary>
    public class StatisticsProjectionService : BackgroundService
    {
        /// <summary>Runs per projector per cycle (× chunk size = most rows caught up per cycle).</summary>
        public const int MaxRunsPerCycle = 50;

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<StatisticsProjectionService> _logger;
        private readonly StatisticsOptions _options;

        public StatisticsProjectionService(IServiceProvider serviceProvider, ILogger<StatisticsProjectionService> logger,
            IOptions<StatisticsOptions>? options = null)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _options = options?.Value ?? new StatisticsOptions();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("Statistics projection disabled (Statistics:Enabled = false)");
                return;
            }
            _logger.LogInformation("Statistics projection started: every {Interval}s", _options.ProjectionIntervalSeconds);
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _options.ProjectionIntervalSeconds)));
                do
                {
                    await RunCycleAsync(DateTime.UtcNow, stoppingToken);
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Statistics projection stopping");
            }
        }

        public async Task RunCycleAsync(DateTime now, CancellationToken ct = default)
        {
            await StepAsync("ledger", sp => DrainAsync(t => sp.GetRequiredService<LedgerStatisticsProjector>().ProjectNextAsync(t), ct), ct);
            await StepAsync("siege", sp => DrainAsync(t => sp.GetRequiredService<SiegeStatisticsProjector>().ProjectNextAsync(t), ct), ct);
            await StepAsync("session-timeout", sp => sp.GetRequiredService<IStatisticsRepository>()
                .CloseTimedOutSessionsAsync(now.AddMinutes(-Math.Max(1, _options.SessionTimeoutMinutes)), ct), ct);
        }

        private static async Task<int> DrainAsync(Func<CancellationToken, Task<int>> run, CancellationToken ct)
        {
            var total = 0;
            for (var i = 0; i < MaxRunsPerCycle && !ct.IsCancellationRequested; i++)
            {
                var count = await run(ct);
                total += count;
                if (count == 0) break;
            }
            return total;
        }

        private async Task StepAsync(string name, Func<IServiceProvider, Task<int>> step, CancellationToken ct)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var count = await step(scope.ServiceProvider);
                if (count > 0)
                {
                    _logger.LogDebug("Statistics {Step}: {Count} processed", name, count);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Statistics {Step} step failed; retrying next cycle", name);
            }
        }
    }
}
