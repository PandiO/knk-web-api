using System;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Statistics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.WorldAnalytics
{
    /// <summary>
    /// Daily world-analytics retention (DESIGN.md §F.15, L1-19): movement cells, menu funnels and
    /// domain interactions older than WorldAnalytics:RetentionDays (180 local days), batch ids older
    /// than BatchRetentionDays (30). Does nothing when WorldAnalytics:Enabled is false.
    /// </summary>
    public class WorldAnalyticsRetentionService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<WorldAnalyticsRetentionService> _logger;
        private readonly WorldAnalyticsOptions _options;
        private readonly TimeZoneInfo _zone;

        public WorldAnalyticsRetentionService(IServiceProvider serviceProvider, ILogger<WorldAnalyticsRetentionService> logger,
            IOptions<WorldAnalyticsOptions>? options = null, IOptions<StatisticsOptions>? statistics = null)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _options = options?.Value ?? new WorldAnalyticsOptions();
            _zone = StatisticsPeriods.FindZone((statistics?.Value ?? new StatisticsOptions()).TimeZone);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("World analytics retention disabled (WorldAnalytics:Enabled = false)");
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
                _logger.LogInformation("World analytics retention stopping");
            }
        }

        public async Task RunOnceAsync(DateTime now, CancellationToken ct = default)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IWorldAnalyticsRepository>();
                var today = StatisticsPeriods.LocalDay(now, _zone);
                var daily = await repo.PurgeDailyBeforeAsync(today.AddDays(-Math.Max(1, _options.RetentionDays)), ct);
                var batches = await repo.PurgeBatchesBeforeAsync(now.AddDays(-Math.Max(1, _options.BatchRetentionDays)), ct);
                _logger.LogInformation("World analytics retention: removed {Daily} daily rows, {Batches} batch ids", daily, batches);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "World analytics retention failed; retrying tomorrow");
            }
        }
    }
}
