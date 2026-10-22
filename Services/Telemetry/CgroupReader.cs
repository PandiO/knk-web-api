using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace knkwebapi_v2.Services.Telemetry
{
    /// <summary>One read of the container's cgroup counters; a null field was not available.</summary>
    public sealed record CgroupSnapshot(
        long? MemoryUsageBytes,
        long? MemoryWorkingSetBytes,
        long? MemoryLimitBytes,
        double? CpuUsageSeconds,
        double? CpuThrottledSeconds,
        long? CpuThrottledPeriods);

    /// <summary>
    /// Reads the memory and CPU counters of the cgroup this process runs in (cgroup v2, with a v1
    /// fallback for memory). A container's limit counts file cache too, so the working set (usage minus
    /// inactive file cache, what cAdvisor reports) is the figure to compare with the limit
    /// (docs/guides/performance-metrics-and-capacity.md § 2). Returns null outside a cgroup.
    /// </summary>
    public sealed class CgroupReader
    {
        public const string DefaultRoot = "/sys/fs/cgroup";

        private readonly string _root;

        public CgroupReader(string? root = null) => _root = root ?? DefaultRoot;

        public CgroupSnapshot? Read()
        {
            try
            {
                return File.Exists(Path.Combine(_root, "memory.current")) ? ReadV2() : ReadV1();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
                return null;
            }
        }

        private CgroupSnapshot ReadV2()
        {
            long? usage = ReadLong("memory.current");
            long? limit = ReadLong("memory.max"); // "max" = unlimited: null
            var mem = ReadKeyed("memory.stat");
            long? working = usage;
            if (usage.HasValue && mem.TryGetValue("inactive_file", out var inactive))
            {
                working = Math.Max(0, usage.Value - (long)inactive);
            }

            var cpu = ReadKeyed("cpu.stat");
            double? used = cpu.TryGetValue("usage_usec", out var u) ? u / 1_000_000d : null;
            double? throttled = cpu.TryGetValue("throttled_usec", out var t) ? t / 1_000_000d : null;
            long? periods = cpu.TryGetValue("nr_throttled", out var n) ? (long)n : null;
            return new CgroupSnapshot(usage, working, limit, used, throttled, periods);
        }

        private CgroupSnapshot? ReadV1()
        {
            var memDir = Path.Combine(_root, "memory");
            if (!File.Exists(Path.Combine(memDir, "memory.usage_in_bytes")))
            {
                return null;
            }

            long? usage = ReadLong(Path.Combine("memory", "memory.usage_in_bytes"));
            long? limit = ReadLong(Path.Combine("memory", "memory.limit_in_bytes"));
            if (limit is >= (1L << 60)) limit = null; // v1 reports "no limit" as a huge number
            var mem = ReadKeyed(Path.Combine("memory", "memory.stat"));
            long? working = usage;
            if (usage.HasValue && mem.TryGetValue("total_inactive_file", out var inactive))
            {
                working = Math.Max(0, usage.Value - (long)inactive);
            }

            var cpuNanos = ReadLong(Path.Combine("cpuacct", "cpuacct.usage"));
            return new CgroupSnapshot(usage, working, limit, cpuNanos / 1_000_000_000d, null, null);
        }

        private long? ReadLong(string relative)
        {
            var path = Path.Combine(_root, relative);
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path).Trim();
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
        }

        private Dictionary<string, double> ReadKeyed(string relative)
        {
            var result = new Dictionary<string, double>();
            var path = Path.Combine(_root, relative);
            if (!File.Exists(path)) return result;
            foreach (var line in File.ReadLines(path))
            {
                var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    result[parts[0]] = v;
                }
            }
            return result;
        }
    }
}
