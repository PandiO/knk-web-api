using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Models;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Telemetry
{
    /// <summary>
    /// Bounded in-memory queue between diagnostic ingestion (requests, middleware) and
    /// <see cref="TelemetryWriterService"/> (KNG-34, IMPLEMENTATION_PLAN.md §4/§7: capacity
    /// DiagnosticTelemetry:QueueCapacity = 10,000). Never blocks a caller: when full, the new event is
    /// dropped and counted (drop-newest); the writer turns the count into a
    /// <c>telemetry.queue_dropped</c> event. Singleton.
    /// </summary>
    public sealed class TelemetryWriteQueue
    {
        private readonly Channel<TelemetryEvent> _channel;
        private readonly TelemetryMetrics? _metrics;
        private long _droppedSinceStart;
        private long _droppedUnreported;
        private long _lastWriteTicks;

        public TelemetryWriteQueue(IOptions<DiagnosticTelemetryOptions>? options = null, TelemetryMetrics? metrics = null)
            : this(Math.Max(1, options?.Value?.QueueCapacity ?? new DiagnosticTelemetryOptions().QueueCapacity), metrics)
        {
        }

        public TelemetryWriteQueue(int capacity, TelemetryMetrics? metrics = null)
        {
            Capacity = capacity;
            _metrics = metrics;
            _channel = Channel.CreateBounded<TelemetryEvent>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait, // with TryWrite: refuse instead of waiting
                SingleReader = true,
                SingleWriter = false
            });
            metrics?.ObserveQueueDepth(() => Depth);
        }

        public int Capacity { get; }

        public int Depth => _channel.Reader.Count;

        public long DroppedSinceStart => Interlocked.Read(ref _droppedSinceStart);

        public DateTime? LastWriteAt
        {
            get
            {
                var ticks = Interlocked.Read(ref _lastWriteTicks);
                return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        /// <summary>Queues an event; false (and counted) when the queue is full.</summary>
        public bool TryEnqueue(TelemetryEvent telemetryEvent)
        {
            if (_channel.Writer.TryWrite(telemetryEvent)) return true;
            RecordDropped(1, "queue_full");
            return false;
        }

        /// <summary>Counts events lost elsewhere in the pipeline (e.g. a failed write).</summary>
        public void RecordDropped(long count, string reason)
        {
            if (count <= 0) return;
            Interlocked.Add(ref _droppedSinceStart, count);
            Interlocked.Add(ref _droppedUnreported, count);
            _metrics?.RecordDropped(reason, count);
        }

        /// <summary>Drops reported since the last call (the writer records them as one event).</summary>
        public long TakeUnreportedDrops() => Interlocked.Exchange(ref _droppedUnreported, 0);

        /// <summary>Takes up to <paramref name="max"/> queued events without waiting.</summary>
        public List<TelemetryEvent> Drain(int max)
        {
            var items = new List<TelemetryEvent>();
            while (items.Count < max && _channel.Reader.TryRead(out var item))
            {
                items.Add(item);
            }
            return items;
        }

        public void MarkWritten(DateTime at) => Interlocked.Exchange(ref _lastWriteTicks, at.Ticks);
    }
}
