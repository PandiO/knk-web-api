namespace knkwebapi_v2.Services.CloudflareAccess
{
    /// <summary>
    /// Reconciles the Cloudflare Access list every CloudflareAccess:ReconcileMinutes (5), and soon
    /// after <see cref="ICloudflareAccessSync.RequestSync"/> (KNG-216, DESIGN.md § 5.8). Does
    /// nothing when the sync is off; logs once and stops when it is on but not configured.
    /// </summary>
    public sealed class CloudflareAccessSyncService : BackgroundService
    {
        /// <summary>A burst of changes (a batch of invites) becomes one run.</summary>
        public static readonly TimeSpan WakeDebounce = TimeSpan.FromSeconds(2);

        private readonly CloudflareAccessSync _sync;
        private readonly TimeProvider _time;
        private readonly ILogger<CloudflareAccessSyncService> _logger;
        private readonly ConnectivityAwareFailureLogger _failures;

        public CloudflareAccessSyncService(CloudflareAccessSync sync, TimeProvider time, ILogger<CloudflareAccessSyncService> logger)
        {
            _sync = sync;
            _time = time;
            _logger = logger;
            _failures = new ConnectivityAwareFailureLogger(logger, "Cloudflare Access sync");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_sync.Settings.SyncEnabled)
            {
                return;
            }
            if (_sync.ConfigurationProblem != null)
            {
                _logger.LogError("Cloudflare Access sync is enabled but can't run: {Problem}", _sync.ConfigurationProblem);
                return;
            }
            _logger.LogInformation("Cloudflare Access sync on: every {Minutes} min and after changes", _sync.Settings.ReconcileInterval.TotalMinutes);

            using var timer = new PeriodicTimer(_sync.Settings.ReconcileInterval, _time);
            Task<bool>? tick = null;
            Task<bool>? wake = null;
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await _sync.ReconcileAsync(stoppingToken);
                        _failures.Succeeded();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _failures.Failed(ex, "Cloudflare Access sync failed");
                    }

                    tick ??= timer.WaitForNextTickAsync(stoppingToken).AsTask();
                    wake ??= _sync.WakeReader.WaitToReadAsync(stoppingToken).AsTask();
                    var done = await Task.WhenAny(tick, wake);
                    if (done == tick)
                    {
                        if (!await tick) break;
                        tick = null;
                    }
                    else
                    {
                        await wake;
                        wake = null;
                        await Task.Delay(WakeDebounce, _time, stoppingToken);
                    }
                    while (_sync.WakeReader.TryRead(out _))
                    {
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }
}
