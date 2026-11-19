using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Services.Production
{
    /// <summary>
    /// The production sweep (P3, PandiO/knk-workspace#86; pattern <see cref="RankExpirySweepService"/>): every
    /// <see cref="DefaultInterval"/> it rolls today's weather per zone and settles the lines whose anchored due time has
    /// passed (indexed <c>NextSettleAt</c>, V2 defect 10). The engine does nothing while <c>Production:Enabled</c> is off
    /// (A21), so the sweep then only reads the setting. The clock is injectable through <see cref="TimeProvider"/>.
    /// </summary>
    public sealed class ProductionSweepService : BackgroundService
    {
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(60);

        private readonly IServiceProvider _services;
        private readonly ILogger<ProductionSweepService> _logger;
        private readonly TimeSpan _interval;
        private readonly ConnectivityAwareFailureLogger _failures;

        public ProductionSweepService(IServiceProvider services, ILogger<ProductionSweepService> logger, TimeSpan? interval = null)
        {
            _services = services;
            _logger = logger;
            _interval = interval ?? DefaultInterval;
            _failures = new ConnectivityAwareFailureLogger(logger, "Production sweep");
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
                        var result = await scope.ServiceProvider.GetRequiredService<IProductionEngine>().SweepAsync(false, stoppingToken);
                        if (result.FeatureEnabled && (result.LinesSettled > 0 || result.WeatherRolled > 0))
                            _logger.LogDebug("Production sweep: {Lines} line(s) settled, {Units} unit(s) accepted, {Rolls} weather roll(s), {Failed} failed",
                                result.LinesSettled, result.UnitsAccepted, result.WeatherRolled, result.LinesFailed);
                        if (result.LinesFailed > 0)
                            _logger.LogWarning("Production sweep: {Failed} line(s) failed to settle", result.LinesFailed);
                        _failures.Succeeded();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _failures.Failed(ex, "Production sweep failed");
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }
}
