using System.Diagnostics.Metrics;

namespace knkwebapi_v2.Realtime;

/// <summary>
/// Meter <c>Knk.Realtime</c> (PandiO/knk-workspace#178): messages published, sent, dropped and failed by hub and type,
/// plus open connections per hub. Exported with the other meters when Telemetry is enabled.
/// </summary>
public sealed class RealtimeMetrics : IDisposable
{
    public const string MeterName = "Knk.Realtime";

    private readonly Meter _meter;
    private readonly Counter<long> _published;
    private readonly Counter<long> _sent;
    private readonly Counter<long> _dropped;
    private readonly Counter<long> _failed;

    public RealtimeMetrics(RealtimeConnections connections)
    {
        _meter = new Meter(MeterName);
        _published = _meter.CreateCounter<long>("knk.realtime.messages.published", description: "Messages queued for the hubs.");
        _sent = _meter.CreateCounter<long>("knk.realtime.messages.sent", description: "Messages sent, one per connection.");
        _dropped = _meter.CreateCounter<long>("knk.realtime.messages.dropped", description: "Messages dropped because the queue was full.");
        _failed = _meter.CreateCounter<long>("knk.realtime.messages.failed", description: "Sends that threw.");
        _meter.CreateObservableGauge("knk.realtime.connections", () => new[]
        {
            new Measurement<int>(connections.Count(RealtimeHubKind.Game), new KeyValuePair<string, object?>("hub", "game")),
            new Measurement<int>(connections.Count(RealtimeHubKind.Web), new KeyValuePair<string, object?>("hub", "web")),
        }, description: "Open hub connections.");
    }

    public void Published(RealtimeHubKind hub, string type) => _published.Add(1, Tags(hub, type));
    public void Sent(RealtimeHubKind hub, string type) => _sent.Add(1, Tags(hub, type));
    public void Dropped(RealtimeHubKind hub, string type) => _dropped.Add(1, Tags(hub, type));
    public void Failed(RealtimeHubKind hub, string type) => _failed.Add(1, Tags(hub, type));

    private static KeyValuePair<string, object?>[] Tags(RealtimeHubKind hub, string type) => new[]
    {
        new KeyValuePair<string, object?>("hub", hub == RealtimeHubKind.Game ? "game" : "web"),
        new KeyValuePair<string, object?>("type", type),
    };

    public void Dispose() => _meter.Dispose();
}
