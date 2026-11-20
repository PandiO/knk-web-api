using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Services.Production
{
    /// <summary>
    /// The transport sweep (P5, PandiO/knk-workspace#79; pattern <see cref="ProductionSweepService"/>): every
    /// <see cref="DefaultInterval"/> it plans automatic orders for producers whose output reached a trigger and moves every
    /// due order along (indexed <c>NextActionAt</c>). Nothing runs while <c>Production:Enabled</c> is off (A21); state
    /// lives in the database, so a restart continues mid-transit.
    /// </summary>
    public sealed class TransportSweepService : BackgroundService
    {
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(60);

        private readonly IServiceProvider _services;
        private readonly ILogger<TransportSweepService> _logger;
        private readonly TimeSpan _interval;
        private readonly ConnectivityAwareFailureLogger _failures;

        public TransportSweepService(IServiceProvider services, ILogger<TransportSweepService> logger, TimeSpan? interval = null)
        {
            _services = services;
            _logger = logger;
            _interval = interval ?? DefaultInterval;
            _failures = new ConnectivityAwareFailureLogger(logger, "Transport sweep");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(_interval);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    try
                    {
                        using var scope = _services.CreateScope();
                        var result = await scope.ServiceProvider.GetRequiredService<ITransportService>().SweepAsync(false, stoppingToken);
                        if (result.FeatureEnabled && (result.OrdersCreated > 0 || result.OrdersAdvanced > 0))
                            _logger.LogDebug("Transport sweep: {Created} order(s) planned, {Advanced} moved along, {Failed} failed",
                                result.OrdersCreated, result.OrdersAdvanced, result.OrdersFailed);
                        if (result.OrdersFailed > 0)
                            _logger.LogWarning("Transport sweep: {Failed} order step(s) failed", result.OrdersFailed);
                        _failures.Succeeded();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _failures.Failed(ex, "Transport sweep failed");
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }
}
