using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace knkwebapi_v2.Services.Statistics
{
    /// <summary>
    /// OpenTelemetry meter "Knk.Statistics" (KNG-34, IMPLEMENTATION_PLAN.md §4). No player ids in
    /// tags (OBSERVABILITY.md): only rejection codes and projector names.
    /// </summary>
    public sealed class StatisticsMetrics : IDisposable
    {
        public const string MeterName = "Knk.Statistics";

        private readonly Counter<long> _batches;
        private readonly Counter<long> _entries;
        private readonly Counter<long> _rejected;
        private readonly Histogram<double> _projectionLag;

        public StatisticsMetrics()
        {
            Meter = new Meter(MeterName);
            _batches = Meter.CreateCounter<long>("knk.statistics.batches", "{batch}",
                "Plugin statistics batches received, tagged duplicate=true for replays of an ingested batch id.");
            _entries = Meter.CreateCounter<long>("knk.statistics.entries", "{entry}",
                "Statistics batch entries accepted.");
            _rejected = Meter.CreateCounter<long>("knk.statistics.rejected", "{entry}",
                "Statistics batch entries rejected, per rejection code.");
            _projectionLag = Meter.CreateHistogram<double>("knk.statistics.projection.lag_seconds", "s",
                "Age of the newest source record a projector run projected, per projector.");
        }

        public Meter Meter { get; }

        public void RecordBatch(int accepted, IEnumerable<string> rejectionCodes)
        {
            _batches.Add(1, new KeyValuePair<string, object?>("duplicate", false));
            if (accepted > 0) _entries.Add(accepted);
            foreach (var code in rejectionCodes)
            {
                _rejected.Add(1, new KeyValuePair<string, object?>("code", code));
            }
        }

        public void RecordDuplicate() => _batches.Add(1, new KeyValuePair<string, object?>("duplicate", true));

        public void RecordProjectionLag(string projector, TimeSpan lag) =>
            _projectionLag.Record(Math.Max(0, lag.TotalSeconds), new KeyValuePair<string, object?>("projector", projector));

        public void Dispose() => Meter.Dispose();
    }
}
