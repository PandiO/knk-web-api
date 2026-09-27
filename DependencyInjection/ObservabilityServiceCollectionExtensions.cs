using knkwebapi_v2.Configuration;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace knkwebapi_v2.DependencyInjection;

/// <summary>
/// Extension methods for registering observability services.
/// </summary>
public static class ObservabilityServiceCollectionExtensions
{
    /// <summary>
    /// Adds client activity tracking and telemetry services.
    /// </summary>
    public static IServiceCollection AddObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register configuration options.
        services.Configure<ClientActivityOptions>(
            configuration.GetSection(ClientActivityOptions.SectionName));
        services.Configure<TelemetryOptions>(
            configuration.GetSection(TelemetryOptions.SectionName));

        // Get options to configure services.
        var clientActivityOptions = configuration.GetSection(ClientActivityOptions.SectionName)
            .Get<ClientActivityOptions>() ?? new ClientActivityOptions();

        // Register client activity store.
        if (clientActivityOptions.Enabled)
        {
            services.AddSingleton<IClientActivityStore>(provider =>
                new InMemoryClientActivityStore(
                    clientActivityOptions.MaxClients,
                    clientActivityOptions.CleanupInterval));

            // TODO: Register a background service to periodically cleanup old clients
            // services.AddHostedService<ClientActivityCleanupService>();
        }

        // Add health checks.
        services.AddHealthChecks();

        return services;
    }

    /// <summary>
    /// Adds OpenTelemetry instrumentation (tracing, metrics, logging).
    /// </summary>
    public static WebApplicationBuilder AddOpenTelemetryInstrumentation(
        this WebApplicationBuilder builder)
    {
        var telemetryOptions = builder.Configuration.GetSection(TelemetryOptions.SectionName)
            .Get<TelemetryOptions>() ?? new TelemetryOptions();

        if (!telemetryOptions.Enabled)
            return builder;

        // TODO: Add OpenTelemetry setup based on exporter type
        // This is a placeholder for actual OpenTelemetry instrumentation.
        // Implement based on your needs:
        // 
        // if (telemetryOptions.Exporter == "otlp")
        // {
        //     builder.Services
        //         .AddOpenTelemetry()
        //         .WithTracing(tracingBuilder => ...)
        //         .WithMetrics(metricsBuilder => metricsBuilder
        //             .AddMeter(DiscoveryService.MeterName)); // domain discovery counters
        // }
        // else if (telemetryOptions.Exporter == "prometheus")
        // {
        //     // Setup Prometheus metrics exporter
        // }

        builder.Logging.AddOpenTelemetry(logging =>
        {
            // Basic logging instrumentation - can be enhanced
        });

        return builder;
    }
}
