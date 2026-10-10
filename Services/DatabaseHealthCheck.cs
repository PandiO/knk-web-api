using knkwebapi_v2.Properties;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace knkwebapi_v2.Services;

/// <summary>
/// Readiness check (KNG-115): the API is only "ready" when its MySQL database answers.
/// Registered with the <see cref="Tag"/> tag so <c>GET /health/ready</c> runs it while
/// <c>GET /health/live</c> stays a process-only probe.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    public const string Name = "database";
    public const string Tag = "ready";

    private readonly KnKDbContext _db;

    public DatabaseHealthCheck(KnKDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var failureStatus = context.Registration?.FailureStatus ?? HealthStatus.Unhealthy;
        try
        {
            return await _db.Database.CanConnectAsync(cancellationToken)
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
            return new HealthCheckResult(failureStatus, "Database check failed: " + ex.GetType().Name, ex);
        }
    }
}
