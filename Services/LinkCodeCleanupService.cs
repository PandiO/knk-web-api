using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Hourly housekeeping of the linkcodes table (KNG-208): active codes past their ExpiresAt are
    /// marked Expired (the existing <see cref="ILinkCodeService.CleanupExpiredCodesAsync"/>), and
    /// codes that were used or expired more than <see cref="DefaultRetention"/> ago are deleted.
    /// Used and expired codes have no further use; the retention only keeps a short audit trail
    /// (a code's UsedAt next to the account it linked). Never touches an active code.
    /// </summary>
    public class LinkCodeCleanupService : BackgroundService
    {
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);
        public static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(7);

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<LinkCodeCleanupService> _logger;
        private readonly TimeSpan _interval;
        private readonly TimeSpan _retention;
        private readonly ConnectivityAwareFailureLogger _failures;

        public LinkCodeCleanupService(IServiceProvider serviceProvider, ILogger<LinkCodeCleanupService> logger,
            TimeSpan? interval = null, TimeSpan? retention = null)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _interval = interval ?? DefaultInterval;
            _retention = retention ?? DefaultRetention;
            _failures = new ConnectivityAwareFailureLogger(logger, "Link code cleanup");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(_interval);
            try
            {
                do
                {
                    try
                    {
                        var (expired, deleted) = await RunOnceAsync(stoppingToken);
                        if (expired > 0 || deleted > 0)
                        {
                            _logger.LogInformation("Link code cleanup: {Expired} code(s) marked expired, {Deleted} old code(s) deleted", expired, deleted);
                        }
                        _failures.Succeeded();
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _failures.Failed(ex, "Link code cleanup failed");
                    }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }

        /// <summary>One pass: (codes newly marked expired, codes deleted).</summary>
        public async Task<(int Expired, int Deleted)> RunOnceAsync(CancellationToken cancellationToken = default)
        {
            using var scope = _serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ILinkCodeService>();
            var expired = await service.CleanupExpiredCodesAsync();
            var deleted = await service.DeleteFinishedCodesAsync(DateTime.UtcNow - _retention);
            return (expired, deleted);
        }
    }
}
