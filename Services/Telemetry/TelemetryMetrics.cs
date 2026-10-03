using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace knkwebapi_v2.Services.Telemetry
{
    /// <summary>
    /// OpenTelemetry meter "Knk.Telemetry" for the diagnostic pipeline itself (KNG-34 link 6). No
    /// player ids in tags (OBSERVABILITY.md): only sources, rejection codes and drop reasons.
    /// </summary>
    public sealed class TelemetryMetrics : IDisposable
    {
        public const string MeterName = "Knk.Telemetry";

        private readonly Counter<long> _received;
        private readonly Counter<long> _rejected;
        private readonly Counter<long> _dropped;
        private readonly Counter<long> _written;

        public TelemetryMetrics()
        {
            Meter = new Meter(MeterName);
            _received = Meter.CreateCounter<long>("knk.telemetry.events.received", "{event}",
                "Diagnostic events accepted into the write queue, per source.");
            _rejected = Meter.CreateCounter<long>("knk.telemetry.events.rejected", "{event}",
                "Diagnostic events rejected by validation, per code.");
            _dropped = Meter.CreateCounter<long>("knk.telemetry.events.dropped", "{event}",
                "Diagnostic events dropped because the write queue was full or a write failed, per reason.");
            _written = Meter.CreateCounter<long>("knk.telemetry.events.written", "{event}",
                "Diagnostic events written to the database.");
        }

        public Meter Meter { get; }

        /// <summary>Wires the queue-depth gauge (called once by the queue).</summary>
        public void ObserveQueueDepth(Func<int> depth) =>
            Meter.CreateObservableGauge("knk.telemetry.queue.depth", () => depth(), "{event}",
                "Diagnostic events waiting for the writer.");

        public void RecordReceived(string source, int count)
        {
            if (count > 0) _received.Add(count, new KeyValuePair<string, object?>("source", source));
        }

        public void RecordRejected(string code) => _rejected.Add(1, new KeyValuePair<string, object?>("code", code));

        public void RecordDropped(string reason, long count)
        {
            if (count > 0) _dropped.Add(count, new KeyValuePair<string, object?>("reason", reason));
        }

        public void RecordWritten(int count)
        {
            if (count > 0) _written.Add(count);
        }

        public void Dispose() => Meter.Dispose();
    }
}
