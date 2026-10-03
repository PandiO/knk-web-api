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
    /// Daily statistics retention (DESIGN.md §F.15, L1-19): daily rows older than
    /// Statistics:DailyRetentionDays (730; lifetime totals stay), ended sessions older than
    /// SessionRetentionDays (365), batch ids older than BatchRetentionDays (30), kill pairs older
    /// than KillPairRetentionDays (62). Never touches totals, title history, profiles, visibility,
    /// the ledger or Siege tables. Does nothing when Statistics:Enabled is false.
    /// </summary>
    public class StatisticsRetentionService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<StatisticsRetentionService> _logger;
        private readonly StatisticsOptions _options;

        public StatisticsRetentionService(IServiceProvider serviceProvider, ILogger<StatisticsRetentionService> logger,
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
                _logger.LogInformation("Statistics retention disabled (Statistics:Enabled = false)");
                return;
            }
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
                do
                {
                    await RunOnceAsync(DateTime.UtcNow, stoppingToken);
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Statistics retention stopping");
            }
        }

        public async Task RunOnceAsync(DateTime now, CancellationToken ct = default)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IStatisticsRepository>();
                var today = StatisticsPeriods.LocalDay(now, StatisticsPeriods.FindZone(_options.TimeZone));
                var daily = await repo.PurgeDailyBeforeAsync(today.AddDays(-Math.Max(1, _options.DailyRetentionDays)), ct);
                var sessions = await repo.PurgeSessionsStartedBeforeAsync(now.AddDays(-Math.Max(1, _options.SessionRetentionDays)), ct);
                var batches = await repo.PurgeBatchesBeforeAsync(now.AddDays(-Math.Max(1, _options.BatchRetentionDays)), ct);
                var pairs = await repo.PurgeKillPairsBeforeAsync(today.AddDays(-Math.Max(1, _options.KillPairRetentionDays)), ct);
                _logger.LogInformation("Statistics retention: removed {Daily} daily rows, {Sessions} sessions, {Batches} batch ids, {Pairs} kill pairs",
                    daily, sessions, batches, pairs);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Statistics retention failed; retrying tomorrow");
            }
        }
    }
}
