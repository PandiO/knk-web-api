namespace knkwebapi_v2.Configuration;

/// <summary>
/// Configuration for telemetry (OpenTelemetry).
/// </summary>
public class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>
    /// Enable/disable telemetry instrumentation.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Exporter type: "otlp" (OpenTelemetry Protocol) or "prometheus".
    /// </summary>
    public string Exporter { get; set; } = "prometheus";

    /// <summary>
    /// OpenTelemetry Protocol exporter settings.
    /// </summary>
    public OtlpExporterOptions Otlp { get; set; } = new();

    /// <summary>
    /// Prometheus exporter settings.
    /// </summary>
    public PrometheusExporterOptions Prometheus { get; set; } = new();
}

/// <summary>
/// OTLP exporter configuration.
/// </summary>
public class OtlpExporterOptions
{
    /// <summary>
    /// OTLP collector endpoint, e.g. "http://localhost:4317"
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:4317";

    /// <summary>
    /// Enable tracing via OTLP.
    /// </summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>
    /// Enable metrics via OTLP.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Enable logging via OTLP.
    /// </summary>
    public bool EnableLogging { get; set; } = true;
}

/// <summary>
/// Prometheus exporter configuration.
/// </summary>
public class PrometheusExporterOptions
{
    /// <summary>
    /// Endpoint for Prometheus scraping, e.g. "/metrics"
    /// </summary>
    public string Endpoint { get; set; } = "/metrics";

    /// <summary>
    /// Serve the Prometheus scrape endpoint (KNG-232). Off by default; outside Development it also needs
    /// <see cref="BearerToken"/> (the API refuses to start without one). Requires <c>Telemetry:Enabled</c>.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Bearer token a scraper must send (<c>Authorization: Bearer ...</c>); set it with
    /// <c>Telemetry__Prometheus__BearerToken</c>. Empty is only allowed in Development.
    /// </summary>
    public string BearerToken { get; set; } = string.Empty;
}
