using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Statistics;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.WorldAnalytics
{
    public interface IWorldAnalyticsQueryService
    {
        /// <summary>Validates an inclusive local-day range; defaults to the last 7 days. Throws <see cref="ArgumentException"/>.</summary>
        (DateOnly From, DateOnly To) ResolveRange(DateOnly? from, DateOnly? to);

        Task<List<HeatmapWorldDto>> GetWorldsAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

        Task<HeatmapDto> GetHeatmapAsync(string world, DateOnly from, DateOnly to, int? cellSize, CancellationToken ct = default);

        Task<MenuFunnelReportDto> GetMenuFunnelsAsync(string? menuKey, DateOnly from, DateOnly to, CancellationToken ct = default);

        Task<DomainInteractionReportDto> GetDomainsAsync(string? kind, DateOnly from, DateOnly to, CancellationToken ct = default);
    }

    /// <summary>
    /// Owner reads of the world-analytics aggregates (KNG-34 link 7, IMPLEMENTATION_PLAN.md §3.4).
    /// Ranges are inclusive local days (Statistics:TimeZone) of at most WorldAnalytics:MaxRangeDays.
    /// The data is anonymous, so reads are not audited (unlike diagnostic telemetry).
    /// </summary>
    public class WorldAnalyticsQueryService : IWorldAnalyticsQueryService
    {
        public const int DefaultRangeDays = 7;
        public const int DefaultCellSize = 16;

        private readonly IWorldAnalyticsRepository _repository;
        private readonly WorldAnalyticsOptions _options;
        private readonly TimeZoneInfo _zone;
        private readonly Func<DateTime> _clock;

        public WorldAnalyticsQueryService(IWorldAnalyticsRepository repository, IOptions<WorldAnalyticsOptions>? options = null,
            IOptions<StatisticsOptions>? statistics = null)
            : this(repository, options?.Value ?? new WorldAnalyticsOptions(),
                StatisticsPeriods.FindZone((statistics?.Value ?? new StatisticsOptions()).TimeZone), () => DateTime.UtcNow)
        {
        }

        public WorldAnalyticsQueryService(IWorldAnalyticsRepository repository, WorldAnalyticsOptions options, TimeZoneInfo zone,
            Func<DateTime> clock)
        {
            _repository = repository;
            _options = options;
            _zone = zone;
            _clock = clock;
        }

        public (DateOnly From, DateOnly To) ResolveRange(DateOnly? from, DateOnly? to)
        {
            var end = to ?? StatisticsPeriods.LocalDay(_clock(), _zone);
            var start = from ?? end.AddDays(-(DefaultRangeDays - 1));
            if (end < start) throw new ArgumentException("to must not be before from.");
            var days = end.DayNumber - start.DayNumber + 1;
            var max = Math.Max(1, _options.MaxRangeDays);
            if (days > max) throw new ArgumentException($"The range may span at most {max} days (got {days}).");
            return (start, end);
        }

        public async Task<List<HeatmapWorldDto>> GetWorldsAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            var sums = await _repository.GetMovementWorldsAsync(from, to, ct);
            return sums.GroupBy(s => s.World, StringComparer.Ordinal)
                .Select(g => new HeatmapWorldDto
                {
                    World = g.Key,
                    CellSizes = g.Select(s => (int)s.CellSize).Distinct().OrderBy(s => s).ToList(),
                    Samples = g.Sum(s => s.Samples)
                })
                .OrderByDescending(w => w.Samples).ThenBy(w => w.World, StringComparer.Ordinal)
                .ToList();
        }

        public async Task<HeatmapDto> GetHeatmapAsync(string world, DateOnly from, DateOnly to, int? cellSize, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(world)) throw new ArgumentException("world is required.");
            if (cellSize is < 1 or > WorldAnalyticsIngestionService.MaxCellSize)
            {
                throw new ArgumentException($"cellSize must be between 1 and {WorldAnalyticsIngestionService.MaxCellSize}.");
            }
            var rows = await _repository.GetMovementCellsAsync(world.Trim(), from, to, ct);
            var size = cellSize ?? (rows.Count == 0 ? DefaultCellSize : rows.Min(r => (int)r.CellSize));
            var map = Rebucket(rows, size);

            var cells = map.Select(kv => new HeatmapCellDto { X = kv.Key.X, Z = kv.Key.Z, Samples = kv.Value })
                .OrderByDescending(c => c.Samples).ThenBy(c => c.X).ThenBy(c => c.Z)
                .ToList();
            var max = Math.Max(1, _options.MaxHeatmapCells);
            var truncated = cells.Count > max;
            return new HeatmapDto
            {
                World = world.Trim(),
                CellSize = size,
                From = from,
                To = to,
                TotalSamples = cells.Sum(c => c.Samples),
                MaxSamples = cells.Count == 0 ? 0 : cells[0].Samples,
                Truncated = truncated,
                Cells = truncated ? cells.Take(max).ToList() : cells
            };
        }

        /// <summary>
        /// Merges stored cells into cells of <paramref name="size"/> blocks. Only stored sizes that
        /// divide it are used (a 16-block cell fits a 64-block cell exactly; a 48-block cell would not).
        /// </summary>
        public static Dictionary<(int X, int Z), long> Rebucket(IEnumerable<MovementCellSum> rows, int size)
        {
            var map = new Dictionary<(int X, int Z), long>();
            foreach (var row in rows)
            {
                if (row.CellSize < 1 || size % row.CellSize != 0) continue;
                var factor = size / row.CellSize;
                var key = (FloorDiv(row.CellX, factor), FloorDiv(row.CellZ, factor));
                map[key] = map.GetValueOrDefault(key) + row.Samples;
            }
            return map;
        }

        private static int FloorDiv(int value, int divisor)
        {
            var q = value / divisor;
            return (value % divisor != 0 && (value < 0) != (divisor < 0)) ? q - 1 : q;
        }

        public async Task<MenuFunnelReportDto> GetMenuFunnelsAsync(string? menuKey, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            var key = string.IsNullOrWhiteSpace(menuKey) ? null : menuKey.Trim();
            var sums = await _repository.GetMenuStepsAsync(key, from, to, ct);
            var menus = sums.GroupBy(s => s.MenuKey, StringComparer.Ordinal)
                .Select(g =>
                {
                    long Total(string step) => g.Where(s => s.Step == step).Sum(s => s.Count);
                    return new MenuFunnelDto
                    {
                        MenuKey = g.Key,
                        Opened = Total("opened"),
                        Back = Total("back"),
                        Closed = Total("closed"),
                        Steps = g.OrderBy(s => StepOrder(s.Step)).ThenByDescending(s => s.Count)
                            .ThenBy(s => s.Step, StringComparer.Ordinal).ThenBy(s => s.Outcome)
                            .Select(s => new MenuFunnelStepCountDto { Step = s.Step, Outcome = OutcomeName(s.Outcome), Count = s.Count })
                            .ToList()
                    };
                })
                .OrderByDescending(m => m.Opened).ThenBy(m => m.MenuKey, StringComparer.Ordinal)
                .ToList();
            return new MenuFunnelReportDto { From = from, To = to, Menus = menus };
        }

        private static int StepOrder(string step) => step switch
        {
            "opened" => 0,
            "back" => 1,
            "closed" => 2,
            _ => 3
        };

        private static string OutcomeName(TelemetryOutcome outcome) => outcome.ToString().ToLowerInvariant();

        public async Task<DomainInteractionReportDto> GetDomainsAsync(string? kind, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            var filter = string.IsNullOrWhiteSpace(kind) ? null : kind.Trim().ToLowerInvariant();
            if (filter != null && !WorldAnalyticsIngestionService.DomainKinds.Contains(filter))
            {
                throw new ArgumentException("kind must be enter, leave or discover.");
            }
            var rows = await _repository.GetDomainRowsAsync(filter, from, to, ct);
            var labels = await _repository.GetDomainLabelsAsync(rows.Select(r => r.DomainId).Distinct().ToList(), ct);
            var domains = rows.GroupBy(r => r.DomainId)
                .Select(g =>
                {
                    var enters = g.Where(r => r.Kind == "enter").ToList();
                    labels.TryGetValue(g.Key, out var label);
                    return new DomainInteractionSummaryDto
                    {
                        DomainId = g.Key,
                        Name = label?.Name,
                        RegionId = label?.WgRegionId,
                        Enter = enters.Sum(r => (long)r.Count),
                        Leave = g.Where(r => r.Kind == "leave").Sum(r => (long)r.Count),
                        Discover = g.Where(r => r.Kind == "discover").Sum(r => (long)r.Count),
                        VisitorDays = enters.Sum(r => (long)r.UniquePlayers),
                        PeakDailyVisitors = enters.Count == 0 ? 0 : enters.Max(r => r.UniquePlayers)
                    };
                })
                .OrderByDescending(d => d.Enter).ThenByDescending(d => d.Discover).ThenBy(d => d.DomainId)
                .ToList();
            return new DomainInteractionReportDto { From = from, To = to, Kind = filter, Domains = domains };
        }
    }
}
