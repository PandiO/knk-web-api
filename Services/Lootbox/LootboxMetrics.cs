using System.Diagnostics.Metrics;

namespace knkwebapi_v2.Services.Lootbox;

/// <summary>
/// Lootbox counters (docs/specs/lootboxes/DESIGN.md §3.3 "Observability"), exported through the OpenTelemetry
/// pipeline in Program.cs (<c>AddMeter(LootboxMetrics.MeterName)</c>). There is no Prometheus /metrics endpoint;
/// they reach a collector only when Telemetry:Exporter is otlp.
/// </summary>
public static class LootboxMetrics
{
    public const string MeterName = "Knk.Lootboxes";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Spawns = Meter.CreateCounter<long>("lootbox_spawns_total", description: "Boxes spawned");
    private static readonly Counter<long> Claims = Meter.CreateCounter<long>("lootbox_claims_total", description: "Boxes opened (not replays)");
    private static readonly Counter<long> Conflicts = Meter.CreateCounter<long>("lootbox_claim_conflicts_total", description: "Refused claims by reason");
    private static readonly Histogram<double> ClaimDuration = Meter.CreateHistogram<double>("lootbox_claim_duration_ms", unit: "ms", description: "Claim handling time");

    public static void Spawned(string type, int boxStars) =>
        Spawns.Add(1, new("type", type), new("box_stars", boxStars));

    public static void Claimed(string type, int boxStars, int? itemStars, bool special) =>
        Claims.Add(1, new("type", type), new("box_stars", boxStars), new("item_stars", itemStars ?? 0), new("special", special));

    public static void Conflict(string reason) => Conflicts.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public static void ClaimTook(double milliseconds) => ClaimDuration.Record(milliseconds);
}
