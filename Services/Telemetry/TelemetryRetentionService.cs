using System;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Telemetry
{
    /// <summary>
    /// Daily diagnostic retention (DESIGN.md §F.15, L1-19): baseline events older than
    /// DiagnosticTelemetry:BaselineRetentionDays (90), enhanced events older than
    /// EnhancedRetentionDays (14), enhanced targets expired more than 30 days ago. Test runs are kept
    /// (names only). Does nothing when DiagnosticTelemetry:Enabled is false.
    /// </summary>
    public sealed class TelemetryRetentionService : BackgroundService
    {
        public const int ExpiredTargetGraceDays = 30;

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TelemetryRetentionService> _logger;
        private readonly DiagnosticTelemetryOptions _options;

        public TelemetryRetentionService(IServiceProvider serviceProvider, ILogger<TelemetryRetentionService> logger,
            IOptions<DiagnosticTelemetryOptions>? options = null)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _options = options?.Value ?? new DiagnosticTelemetryOptions();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("Diagnostic telemetry retention disabled (DiagnosticTelemetry:Enabled = false)");
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
                _logger.LogInformation("Diagnostic telemetry retention stopping");
            }
        }

        public async Task RunOnceAsync(DateTime now, CancellationToken ct = default)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
                var baseline = await repo.PurgeEventsBeforeAsync(TelemetryLevel.Baseline,
                    now.AddDays(-Math.Max(1, _options.BaselineRetentionDays)), ct);
                var enhanced = await repo.PurgeEventsBeforeAsync(TelemetryLevel.Enhanced,
                    now.AddDays(-Math.Max(1, _options.EnhancedRetentionDays)), ct);
                var targets = await repo.PurgeTargetsExpiredBeforeAsync(now.AddDays(-ExpiredTargetGraceDays), ct);
                _logger.LogInformation("Diagnostic telemetry retention: removed {Baseline} baseline and {Enhanced} enhanced events, {Targets} expired targets",
                    baseline, enhanced, targets);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Diagnostic telemetry retention failed; retrying tomorrow");
            }
        }
    }
}
