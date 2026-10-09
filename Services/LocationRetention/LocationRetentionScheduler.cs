using knkwebapi_v2.Configuration;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.LocationRetention;

/// <summary>
/// Runs the Location orphan check on its schedule (KNG-80), registered like CurrencyMonitorService.
/// Every CheckIntervalMinutes it asks whether a schedule slot (weekly, Sunday 04:00 server time by
/// default) has passed without a scheduled run, and runs it once; a slot missed while the API was
/// down runs when it comes back. Each check uses its own DI scope, and a failure is logged and
/// retried next time - it never stops the loop. Report only: nothing is ever deleted here.
/// </summary>
public class LocationRetentionScheduler : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LocationRetentionScheduler> _logger;
    private readonly LocationRetentionOptions _options;

    public LocationRetentionScheduler(IServiceProvider serviceProvider, ILogger<LocationRetentionScheduler> logger,
        IOptions<LocationRetentionOptions>? options = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options?.Value ?? new LocationRetentionOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Location retention scheduler disabled (LocationRetention:Enabled = false)");
            return;
        }
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, _options.StartupDelaySeconds)), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, _options.CheckIntervalMinutes)));
            do
            {
                await TickAsync(DateTime.UtcNow, stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Location retention scheduler stopping");
        }
    }

    /// <summary>One schedule check at <paramref name="nowUtc"/>. Public so tests can drive it without timers.</summary>
    public async Task TickAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var retention = scope.ServiceProvider.GetRequiredService<ILocationRetentionService>();
            var slot = await retention.DueScheduledSlotAsync(nowUtc, ct);
            if (slot == null) return;
            await retention.RunCheckAsync("scheduled", null, slot, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Location retention schedule check failed; retrying at the next check");
        }
    }
}
