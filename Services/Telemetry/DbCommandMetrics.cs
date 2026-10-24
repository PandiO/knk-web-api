using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace knkwebapi_v2.Services.Telemetry
{
    /// <summary>
    /// OpenTelemetry meter "Knk.Db" (KNG-232): how long database commands take, by kind and outcome. No SQL
    /// text, parameters or player ids in tags. For a reader the duration ends when the reader is returned
    /// (EF Core's CommandExecuted), not when the rows are consumed.
    /// </summary>
    public sealed class DbCommandMetrics : IDisposable
    {
        public const string MeterName = "Knk.Db";
        public const string DurationName = "knk.db.command.duration";

        /// <summary>Seconds; the default OTel buckets are millisecond-scaled and useless for a seconds histogram.</summary>
        public static readonly double[] BucketBoundaries = { 0.0005, 0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10 };

        private readonly Meter _meter = new(MeterName);
        private readonly Histogram<double> _duration;

        public DbCommandMetrics()
        {
            _duration = _meter.CreateHistogram<double>(DurationName, "s", "Database command duration.");
        }

        public void Record(string kind, TimeSpan duration, bool ok) =>
            _duration.Record(duration.TotalSeconds,
                new KeyValuePair<string, object?>("kind", kind),
                new KeyValuePair<string, object?>("outcome", ok ? "ok" : "error"));

        public void Dispose() => _meter.Dispose();
    }

    /// <summary>EF Core interceptor feeding <see cref="DbCommandMetrics"/>.</summary>
    public sealed class DbCommandMetricsInterceptor : DbCommandInterceptor
    {
        private readonly DbCommandMetrics _metrics;

        public DbCommandMetricsInterceptor(DbCommandMetrics metrics) => _metrics = metrics;

        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            _metrics.Record("reader", eventData.Duration, true);
            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            _metrics.Record("reader", eventData.Duration, true);
            return new ValueTask<DbDataReader>(result);
        }

        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
        {
            _metrics.Record("write", eventData.Duration, true);
            return result;
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            _metrics.Record("write", eventData.Duration, true);
            return new ValueTask<int>(result);
        }

        public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
        {
            _metrics.Record("scalar", eventData.Duration, true);
            return result;
        }

        public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            object? result, CancellationToken cancellationToken = default)
        {
            _metrics.Record("scalar", eventData.Duration, true);
            return new ValueTask<object?>(result);
        }

        public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) =>
            _metrics.Record("command", eventData.Duration, false);

        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            _metrics.Record("command", eventData.Duration, false);
            return Task.CompletedTask;
        }
    }
}
