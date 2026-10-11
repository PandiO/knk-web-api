using System;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Privacy
{
    /// <summary>
    /// Hourly GDPR deletion job (DESIGN.md §F.14, developer decisions 2026-10-03): expires
    /// confirmation links that were not used in time and executes confirmed requests whose grace
    /// period (Privacy:GraceDays) is over. It never acts on a player without such a request. With
    /// Privacy:AutoExecuteEnabled false it only expires links; the owner executes by hand.
    /// </summary>
    public sealed class PrivacyDeletionDueService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PrivacyDeletionDueService> _logger;
        private readonly PrivacyOptions _options;

        public PrivacyDeletionDueService(IServiceProvider serviceProvider, ILogger<PrivacyDeletionDueService> logger,
            IOptions<PrivacyOptions>? options = null)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _options = options?.Value ?? new PrivacyOptions();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.AutoExecuteEnabled)
            {
                _logger.LogInformation("GDPR deletion auto-execution disabled (Privacy:AutoExecuteEnabled = false); only expiring confirmation links");
            }
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
                do
                {
                    await RunOnceAsync(DateTime.UtcNow, stoppingToken);
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("GDPR deletion job stopping");
            }
        }

        public async Task<int> RunOnceAsync(DateTime now, CancellationToken ct = default)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IPrivacyDeletionService>();
                var expired = await service.ExpireConfirmationsAsync(now, ct);
                if (expired > 0) _logger.LogInformation("GDPR deletion: {Count} unconfirmed request(s) expired", expired);
                var executed = await service.ExecuteScheduledAsync(now, ct);
                if (executed > 0) _logger.LogWarning("GDPR deletion: executed {Count} request(s) after their grace period", executed);
                return executed;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GDPR deletion job failed; retrying in an hour");
                return 0;
            }
        }
    }
}
