using knkwebapi_v2.Services;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Realtime.Presence;

/// <summary>
/// Ends the grace period of disconnected game servers (DESIGN §5, Q5): a server that has not reconnected within
/// <see cref="RealtimeOptions.PresenceGraceSeconds"/> loses its players, who are marked offline unless they are online
/// on another server.
/// </summary>
public sealed class PresenceSweepService : BackgroundService
{
    private readonly PresenceRegistry _registry;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PresenceSweepService> _logger;
    private readonly RealtimeOptions _options;
    private readonly TimeProvider _time;
    private readonly ConnectivityAwareFailureLogger _failures;

    public PresenceSweepService(PresenceRegistry registry, IServiceScopeFactory scopes, IOptions<RealtimeOptions> options,
        ILogger<PresenceSweepService> logger, TimeProvider? time = null)
    {
        _registry = registry;
        _scopes = scopes;
        _logger = logger;
        _options = options.Value;
        _time = time ?? TimeProvider.System;
        _failures = new ConnectivityAwareFailureLogger(logger, "Realtime presence sweep");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.PresenceSweepSeconds)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                    _failures.Succeeded();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _failures.Failed(ex, "Realtime presence sweep failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>One pass; returns how many users were marked offline.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var expired = _registry.ExpireDisconnected(TimeSpan.FromSeconds(Math.Max(0, _options.PresenceGraceSeconds)));
        if (expired.Count == 0) return 0;
        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPresenceStore>();
        var total = 0;
        foreach (var server in expired)
        {
            var changed = await store.SetOfflineAsync(server.OfflineUuids, _time.GetUtcNow().UtcDateTime, cancellationToken);
            total += changed;
            _logger.LogInformation(
                "Game server {ServerId} did not reconnect within {Grace}s: {Count} player(s) marked offline",
                server.ServerId, _options.PresenceGraceSeconds, changed);
        }
        return total;
    }
}
