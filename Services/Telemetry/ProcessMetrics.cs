using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace knkwebapi_v2.Services.Telemetry
{
    /// <summary>
    /// OpenTelemetry meter "Knk.Process" (KNG-232): this process's CPU time and memory, plus the container's
    /// cgroup memory/CPU counters, so API cost can be compared with the container limit and with the
    /// Minecraft container on the same host. The runtime meter (GC, heap, thread pool) comes from the
    /// OpenTelemetry runtime instrumentation. No labels beyond the instrument names.
    /// </summary>
    public sealed class ProcessMetrics : IDisposable
    {
        public const string MeterName = "Knk.Process";

        private readonly Meter _meter;
        private readonly CgroupReader _cgroup;

        public ProcessMetrics() : this(new CgroupReader())
        {
        }

        public ProcessMetrics(CgroupReader cgroup)
        {
            _cgroup = cgroup;
            _meter = new Meter(MeterName);

            _meter.CreateObservableCounter("knk.process.cpu.time", () =>
            {
                using var p = Process.GetCurrentProcess();
                return p.TotalProcessorTime.TotalSeconds;
            }, "s", "CPU time used by the API process.");
            _meter.CreateObservableGauge("knk.process.memory.working_set", () =>
            {
                using var p = Process.GetCurrentProcess();
                return p.WorkingSet64;
            }, "By", "Resident memory of the API process.");

            _meter.CreateObservableGauge("knk.cgroup.memory.usage", () => Measure(s => s.MemoryUsageBytes), "By",
                "Container memory usage including reclaimable file cache.");
            _meter.CreateObservableGauge("knk.cgroup.memory.working_set", () => Measure(s => s.MemoryWorkingSetBytes), "By",
                "Container memory usage minus inactive file cache (compare with the limit).");
            _meter.CreateObservableGauge("knk.cgroup.memory.limit", () => Measure(s => s.MemoryLimitBytes), "By",
                "Container memory limit; absent when unlimited.");
            _meter.CreateObservableCounter("knk.cgroup.cpu.time", () => Measure(s => s.CpuUsageSeconds), "s",
                "Container CPU time.");
            _meter.CreateObservableCounter("knk.cgroup.cpu.throttled_time", () => Measure(s => s.CpuThrottledSeconds), "s",
                "Time the container was throttled by its CPU limit.");
        }

        private IEnumerable<Measurement<double>> Measure(Func<CgroupSnapshot, double?> pick)
        {
            var snapshot = _cgroup.Read();
            if (snapshot != null && pick(snapshot) is { } value)
            {
                yield return new Measurement<double>(value);
            }
        }

        private IEnumerable<Measurement<long>> Measure(Func<CgroupSnapshot, long?> pick)
        {
            var snapshot = _cgroup.Read();
            if (snapshot != null && pick(snapshot) is { } value)
            {
                yield return new Measurement<long>(value);
            }
        }

        public void Dispose() => _meter.Dispose();
    }
}
