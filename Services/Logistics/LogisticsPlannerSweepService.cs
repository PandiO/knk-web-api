using System;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Services.Production;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Services.Logistics
{
    /// <summary>
    /// The planner's schedule (P9, PandiO/knk-workspace#127; pattern <see cref="TransportSweepService"/>): every
    /// <see cref="DefaultInterval"/> it plans each town whose last run is at least <c>production.logistics.plannerIntervalMinutes</c>
    /// (10, D-L2) ago and prunes old runs. Nothing runs while <c>Production:Enabled</c> is off (A21).
    /// </summary>
    public sealed class LogisticsPlannerSweepService : BackgroundService
    {
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(60);

        private readonly IServiceProvider _services;
        private readonly ILogger<LogisticsPlannerSweepService> _logger;
        private readonly TimeSpan _interval;
        private readonly ConnectivityAwareFailureLogger _failures;

        public LogisticsPlannerSweepService(IServiceProvider services, ILogger<LogisticsPlannerSweepService> logger, TimeSpan? interval = null)
        {
            _services = services;
            _logger = logger;
            _interval = interval ?? DefaultInterval;
            _failures = new ConnectivityAwareFailureLogger(logger, "Logistics planner");
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
                        var result = await scope.ServiceProvider.GetRequiredService<ILogisticsPlanner>().SweepAsync(stoppingToken);
                        if (result.FeatureEnabled && result.TownsPlanned > 0)
                            _logger.LogDebug("Logistics planner: {Towns} town(s) planned, {Reserved} reservation(s), {Orders} collection order(s), {Released} released",
                                result.TownsPlanned, result.ReservationsCreated, result.OrdersCreated, result.ReservationsReleased);
                        if (result.Failed > 0) _logger.LogWarning("Logistics planner: {Failed} town run(s) failed", result.Failed);
                        _failures.Succeeded();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _failures.Failed(ex, "Logistics planner sweep failed");
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }
}
