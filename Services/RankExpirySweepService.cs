using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Every 30 seconds: users whose temporary rank expired go back to the free Default rank, and
    /// the plugin is told (RankChanged notification) so their chat and tab-list rank update while
    /// they're online instead of on their next join. See docs/specs/user-features/RANK_DISPLAY.md.
    /// </summary>
    public class RankExpirySweepService : BackgroundService
    {
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(30);

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<RankExpirySweepService> _logger;
        private readonly TimeSpan _interval;

        public RankExpirySweepService(IServiceProvider serviceProvider, ILogger<RankExpirySweepService> logger,
            TimeSpan? interval = null)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _interval = interval ?? DefaultInterval;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Expiries while the API was down: the first sweep still restores Default for anyone left
            // without a rank, but only notifies from now on (the plugin re-reads players on join anyway).
            var since = DateTime.UtcNow;
            using var timer = new PeriodicTimer(_interval);
            try
            {
                do
                {
                    var now = DateTime.UtcNow;
                    try
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var service = scope.ServiceProvider.GetRequiredService<IUserPermissionGroupService>();
                        var notified = await service.SweepExpiredRanksAsync(since, now);
                        if (notified > 0)
                            _logger.LogInformation("Rank expiry sweep: {Count} user(s) updated/notified", notified);
                        since = now;
                    }
                    catch (Exception ex)
                    {
                        // Keep `since` so the next sweep covers this window again.
                        _logger.LogError(ex, "Rank expiry sweep failed");
                    }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }
}
