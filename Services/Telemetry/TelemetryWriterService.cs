using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.Telemetry
{
    /// <summary>
    /// Drains <see cref="TelemetryWriteQueue"/> every DiagnosticTelemetry:WriteIntervalSeconds (2 s)
    /// and batch-inserts the events (KNG-34, IMPLEMENTATION_PLAN.md §4/§7). Requests never wait for
    /// this write. A failed write drops that batch (counted, never retried: L1-23). Drops are
    /// reported as one <c>telemetry.queue_dropped</c> event per flush. Idle when
    /// DiagnosticTelemetry:Enabled is false.
    /// </summary>
    public sealed class TelemetryWriterService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly TelemetryWriteQueue _queue;
        private readonly ILogger<TelemetryWriterService> _logger;
        private readonly TelemetryMetrics? _metrics;
        private readonly DiagnosticTelemetryOptions _options;
        private long _sequence;

        public TelemetryWriterService(IServiceProvider serviceProvider, TelemetryWriteQueue queue,
            ILogger<TelemetryWriterService> logger, IOptions<DiagnosticTelemetryOptions>? options = null,
            TelemetryMetrics? metrics = null)
        {
            _serviceProvider = serviceProvider;
            _queue = queue;
            _logger = logger;
            _metrics = metrics;
            _options = options?.Value ?? new DiagnosticTelemetryOptions();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("Diagnostic telemetry writer disabled (DiagnosticTelemetry:Enabled = false)");
                return;
            }
            _logger.LogInformation("Diagnostic telemetry writer started: every {Seconds}s, queue capacity {Capacity}",
                Math.Max(1, _options.WriteIntervalSeconds), _queue.Capacity);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.WriteIntervalSeconds)));
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await FlushAsync(DateTime.UtcNow, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Last flush on shutdown so a clean stop loses nothing that was queued.
                await FlushAsync(DateTime.UtcNow, CancellationToken.None);
                _logger.LogInformation("Diagnostic telemetry writer stopping");
            }
        }

        /// <summary>Writes everything queued now (in chunks); returns the number of events written.</summary>
        public async Task<int> FlushAsync(DateTime now, CancellationToken ct = default)
        {
            var written = 0;
            var batchSize = Math.Max(1, _options.WriteBatchSize);
            while (true)
            {
                var batch = _queue.Drain(batchSize);
                var drops = _queue.TakeUnreportedDrops();
                if (drops > 0) batch.Add(DroppedEvent(drops, now));
                if (batch.Count == 0) return written;
                written += await WriteAsync(batch, ct);
                if (batch.Count < batchSize) return written;
            }
        }

        private async Task<int> WriteAsync(List<TelemetryEvent> batch, CancellationToken ct)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
                int count;
                try
                {
                    count = await repository.InsertEventsAsync(batch, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Usually a duplicate id inserted by another API instance in between: retry one by
                    // one in a fresh scope so one row can't sink the batch.
                    count = 0;
                    foreach (var item in batch)
                    {
                        using var single = _serviceProvider.CreateScope();
                        item.Id = 0;
                        try
                        {
                            count += await single.ServiceProvider.GetRequiredService<ITelemetryRepository>()
                                .InsertEventsAsync(new[] { item }, ct);
                        }
                        catch (Exception rowEx) when (rowEx is not OperationCanceledException)
                        {
                            _queue.RecordDropped(1, "write_failed");
                            _logger.LogDebug(rowEx, "Dropped diagnostic event {Name}", item.Name);
                        }
                    }
                }
                _queue.MarkWritten(DateTime.UtcNow);
                _metrics?.RecordWritten(count);
                return count;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _queue.RecordDropped(batch.Count, "write_failed");
                _logger.LogWarning(ex, "Diagnostic telemetry write failed; dropped {Count} events", batch.Count);
                return 0;
            }
        }

        private TelemetryEvent DroppedEvent(long dropped, DateTime now) => new()
        {
            EventId = Guid.NewGuid(),
            Name = "telemetry.queue_dropped",
            SchemaVersion = 1,
            Level = TelemetryLevel.Baseline,
            Source = TelemetrySource.Api,
            OccurredAt = now,
            ReceivedAt = now,
            ServerName = TelemetryIngestionService.ApiServerName,
            ServerSeq = Interlocked.Increment(ref _sequence),
            AppVersion = TelemetryIngestionService.ApiVersion.Length > 32 ? TelemetryIngestionService.ApiVersion[..32] : TelemetryIngestionService.ApiVersion,
            Feature = "telemetry",
            Action = "queue_dropped",
            Outcome = TelemetryOutcome.Info,
            ReasonCode = "dropped",
            PayloadJson = $"{{\"dropped\":{dropped}}}"
        };
    }
}
