using knkwebapi_v2.Properties;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace knkwebapi_v2.Services;

/// <summary>
/// Readiness check (KNG-115): the API is only "ready" when its MySQL database answers.
/// Registered with the <see cref="Tag"/> tag so <c>GET /health/ready</c> runs it while
/// <c>GET /health/live</c> stays a process-only probe.
/// <para>
/// The DbContext is resolved inside the check, not injected: HealthCheckService creates the check
/// outside its own exception handling, so a DbContext that fails to build (as it did while
/// Program.cs ran <c>ServerVersion.AutoDetect</c> per DbContext) would turn "database down" into
/// a 500 instead of "unhealthy".
/// </para>
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    public const string Name = "database";
    public const string Tag = "ready";

    private readonly IServiceProvider _services;

    public DatabaseHealthCheck(IServiceProvider services)
    {
        _services = services;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var failureStatus = context.Registration?.FailureStatus ?? HealthStatus.Unhealthy;
        try
        {
            var db = _services.GetRequiredService<KnKDbContext>();
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Database reachable")
                : new HealthCheckResult(failureStatus, "Database not reachable");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The registration's timeout fired; HealthCheckService reports that itself.
            throw;
        }
        catch (Exception ex)
        {
            // No exception attached: HealthCheckService would log its full stack on every probe
            // (every 10 s from each game server) for as long as MySQL is down.
            return new HealthCheckResult(failureStatus, $"Database check failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
