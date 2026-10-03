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
    /// Daily GDPR deadline guard (DESIGN.md §F.14, L1-18): executes requests still pending
    /// Privacy:AutoExecuteBeforeDueDays (3) days before their due date, so the one-month deadline
    /// (Art. 12(3)) cannot pass silently. Idle when Privacy:AutoExecuteEnabled is false.
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
                _logger.LogInformation("GDPR deletion auto-execution disabled (Privacy:AutoExecuteEnabled = false)");
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
                _logger.LogInformation("GDPR deletion due job stopping");
            }
        }

        public async Task<int> RunOnceAsync(DateTime now, CancellationToken ct = default)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IPrivacyDeletionService>();
                var executed = await service.ExecuteDueAsync(now, ct);
                if (executed > 0) _logger.LogWarning("GDPR deletion: auto-executed {Count} request(s) close to their due date", executed);
                return executed;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GDPR deletion due job failed; retrying tomorrow");
                return 0;
            }
        }
    }
}
