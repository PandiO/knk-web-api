using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Leaderboards
{
    /// <summary>
    /// Refreshes all leaderboard snapshots every Leaderboards:RefreshSeconds (default 300; DESIGN.md
    /// §F.11) in its own scope. Reads never compute rankings. Leaderboards:Enabled = false → idle
    /// (reads keep serving the last snapshots).
    /// </summary>
    public class LeaderboardSnapshotService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<LeaderboardSnapshotService> _logger;
        private readonly LeaderboardsOptions _options;

        public LeaderboardSnapshotService(IServiceProvider serviceProvider, ILogger<LeaderboardSnapshotService> logger,
            IOptions<LeaderboardsOptions>? options = null)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _options = options?.Value ?? new LeaderboardsOptions();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("Leaderboard snapshots disabled (Leaderboards:Enabled = false)");
                return;
            }
            _logger.LogInformation("Leaderboard snapshots started: every {Interval}s", _options.RefreshSeconds);
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(30, _options.RefreshSeconds)));
                do
                {
                    await RefreshAsync(stoppingToken);
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Leaderboard snapshots stopping");
            }
        }

        public async Task RefreshAsync(CancellationToken ct = default)
        {
            try
            {
                var watch = Stopwatch.StartNew();
                using var scope = _serviceProvider.CreateScope();
                var count = await scope.ServiceProvider.GetRequiredService<LeaderboardSnapshotBuilder>().BuildAllAsync(ct);
                _logger.LogDebug("Leaderboards: {Count} snapshots in {Elapsed} ms", count, watch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Leaderboard refresh failed; retrying next cycle");
            }
        }
    }
}
