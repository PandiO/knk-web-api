using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Statistics;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.WorldAnalytics
{
    public interface IWorldAnalyticsIngestionService
    {
        /// <summary>Validates and applies one plugin batch. Throws <see cref="ArgumentException"/> for a structurally invalid batch (400).</summary>
        Task<WorldAnalyticsBatchResultDto> IngestAsync(WorldAnalyticsBatchDto batch, CancellationToken ct = default);
    }

    /// <summary>
    /// Ingests plugin world-analytics batches (KNG-34 link 7, knk-workspace
    /// docs/specs/player-statistics/IMPLEMENTATION_PLAN.md §3.4). One transaction per batch: the batch
    /// row is inserted first, so a retry with the same batch id applies nothing. Every row lands on the
    /// local day (Statistics:TimeZone) of <c>windowStart</c>; rows are validated one by one, the valid
    /// ones pre-aggregated per key and written with multi-row upserts. Nothing in a batch identifies a
    /// player.
    /// </summary>
    public class WorldAnalyticsIngestionService : IWorldAnalyticsIngestionService
    {
        public const string MovementSection = "movementCells";
        public const string MenuSection = "menuSteps";
        public const string DomainSection = "domainInteractions";

        public const int MaxCellSize = 1024;
        public const int MaxCount = 10_000_000;

        public static readonly IReadOnlyList<string> DomainKinds = new[] { "enter", "leave", "discover" };

        // Lower-case ids like the menu framework's action type ids (e.g. "statistics.visibility.cycle").
        private static readonly Regex ActionId = new("^[a-z0-9][a-z0-9._:-]{0,87}$", RegexOptions.CultureInvariant);
        private static readonly Regex MenuKeyPattern = new("^[A-Za-z0-9][A-Za-z0-9._:-]{0,190}$", RegexOptions.CultureInvariant);

        private readonly IWorldAnalyticsRepository _repository;
        private readonly WorldAnalyticsOptions _options;
        private readonly TimeZoneInfo _zone;
        private readonly Func<DateTime> _clock;

        public WorldAnalyticsIngestionService(IWorldAnalyticsRepository repository, IOptions<WorldAnalyticsOptions>? options = null,
            IOptions<StatisticsOptions>? statistics = null)
            : this(repository, options?.Value ?? new WorldAnalyticsOptions(),
                StatisticsPeriods.FindZone((statistics?.Value ?? new StatisticsOptions()).TimeZone), () => DateTime.UtcNow)
        {
        }

        public WorldAnalyticsIngestionService(IWorldAnalyticsRepository repository, WorldAnalyticsOptions options, TimeZoneInfo zone,
            Func<DateTime> clock)
        {
            _repository = repository;
            _options = options;
            _zone = zone;
            _clock = clock;
        }

        public async Task<WorldAnalyticsBatchResultDto> IngestAsync(WorldAnalyticsBatchDto batch, CancellationToken ct = default)
        {
            if (batch == null) throw new ArgumentException("A batch body is required.");
            if (batch.BatchId == Guid.Empty) throw new ArgumentException("batchId is required.");
            if (batch.WindowStart == default) throw new ArgumentException("windowStart is required.");
            var cells = batch.MovementCells ?? new List<WorldMovementCellDto>();
            var steps = batch.MenuSteps ?? new List<MenuFunnelStepDto>();
            var domains = batch.DomainInteractions ?? new List<DomainInteractionDto>();
            var rowCount = cells.Count + steps.Count + domains.Count;
            if (rowCount > _options.MaxBatchRows)
            {
                throw new ArgumentException($"A batch may carry at most {_options.MaxBatchRows} rows (got {rowCount}).");
            }

            var now = _clock();
            var windowStart = AsUtc(batch.WindowStart);
            if (windowStart > now.AddMinutes(5))
            {
                throw new ArgumentException("windowStart lies in the future.");
            }
            if (windowStart < now.AddDays(-Math.Max(1, _options.LateBatchToleranceDays)))
            {
                throw new ArgumentException($"windowStart is older than {_options.LateBatchToleranceDays} days.");
            }
            var day = StatisticsPeriods.LocalDay(windowStart, _zone);
            var serverName = Truncate(batch.ServerName, 64);

            return await _repository.InTransactionAsync(async () =>
            {
                var result = new WorldAnalyticsBatchResultDto { BatchId = batch.BatchId, Day = day };
                if (await _repository.BatchExistsAsync(batch.BatchId, ct))
                {
                    result.Duplicate = true;
                    return result;
                }

                var deltas = new WorldAnalyticsDeltas();
                AddCells(cells, deltas, result);
                AddSteps(steps, deltas, result);
                await AddDomainsAsync(domains, deltas, result, ct);
                result.Accepted = rowCount - result.Rejected.Count;

                var inserted = await _repository.TryAddBatchAsync(new WorldAnalyticsBatch
                {
                    BatchId = batch.BatchId,
                    ServerName = serverName,
                    ReceivedAt = now,
                    WindowStart = windowStart,
                    RowCount = rowCount,
                    RejectedCount = result.Rejected.Count
                }, ct);
                if (!inserted)
                {
                    // A concurrent request with the same batch id won the insert; it applies the batch.
                    return new WorldAnalyticsBatchResultDto { BatchId = batch.BatchId, Day = day, Duplicate = true };
                }

                await _repository.ApplyAsync(day, deltas, ct);
                return result;
            }, ct);
        }

        private static void AddCells(List<WorldMovementCellDto> cells, WorldAnalyticsDeltas deltas, WorldAnalyticsBatchResultDto result)
        {
            for (var i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                var code = cell == null ? "InvalidRow"
                    : string.IsNullOrWhiteSpace(cell.World) || cell.World.Length > 64 ? "InvalidWorld"
                    : cell.CellSize < 1 || cell.CellSize > MaxCellSize ? "InvalidCellSize"
                    : cell.Samples < 1 || cell.Samples > MaxCount ? "InvalidCount"
                    : null;
                if (code != null)
                {
                    Reject(result, MovementSection, i, code);
                    continue;
                }
                var key = new MovementCellKey(cell!.World.Trim(), (short)cell.CellSize, cell.CellX, cell.CellZ);
                deltas.Cells[key] = Saturating(deltas.Cells.GetValueOrDefault(key), cell.Samples);
            }
        }

        private static void AddSteps(List<MenuFunnelStepDto> steps, WorldAnalyticsDeltas deltas, WorldAnalyticsBatchResultDto result)
        {
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (step == null)
                {
                    Reject(result, MenuSection, i, "InvalidRow");
                    continue;
                }
                var menuKey = step.MenuKey?.Trim() ?? "";
                var stepName = step.Step?.Trim() ?? "";
                if (!MenuKeyPattern.IsMatch(menuKey))
                {
                    Reject(result, MenuSection, i, "InvalidMenuKey");
                    continue;
                }
                if (!IsValidStep(stepName))
                {
                    Reject(result, MenuSection, i, "InvalidStep");
                    continue;
                }
                if (!TryParseOutcome(step.Outcome, stepName, out var outcome))
                {
                    Reject(result, MenuSection, i, "InvalidOutcome");
                    continue;
                }
                if (step.Count < 1 || step.Count > MaxCount)
                {
                    Reject(result, MenuSection, i, "InvalidCount");
                    continue;
                }
                var key = new MenuStepKey(menuKey, stepName, outcome);
                deltas.MenuSteps[key] = Saturating(deltas.MenuSteps.GetValueOrDefault(key), step.Count);
            }
        }

        private async Task AddDomainsAsync(List<DomainInteractionDto> rows, WorldAnalyticsDeltas deltas, WorldAnalyticsBatchResultDto result,
            CancellationToken ct)
        {
            var regionIds = rows
                .Where(r => r != null && (r.DomainId ?? 0) <= 0 && !string.IsNullOrWhiteSpace(r.RegionId))
                .Select(r => r.RegionId!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var resolved = await _repository.ResolveDomainIdsAsync(regionIds, ct);

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null)
                {
                    Reject(result, DomainSection, i, "InvalidRow");
                    continue;
                }
                var kind = row.Kind?.Trim().ToLowerInvariant() ?? "";
                if (!DomainKinds.Contains(kind))
                {
                    Reject(result, DomainSection, i, "InvalidKind");
                    continue;
                }
                if (row.Count < 0 || row.Count > MaxCount || row.UniquePlayers < 0 || row.UniquePlayers > MaxCount
                    || (row.Count == 0 && row.UniquePlayers == 0))
                {
                    Reject(result, DomainSection, i, "InvalidCount");
                    continue;
                }
                int domainId;
                if (row.DomainId is > 0)
                {
                    domainId = row.DomainId.Value;
                }
                else if (string.IsNullOrWhiteSpace(row.RegionId))
                {
                    Reject(result, DomainSection, i, "MissingDomain");
                    continue;
                }
                else if (!resolved.TryGetValue(row.RegionId.Trim(), out domainId))
                {
                    Reject(result, DomainSection, i, "UnknownRegion");
                    continue;
                }
                var key = new DomainInteractionKey(domainId, kind);
                var current = deltas.Domains.GetValueOrDefault(key);
                deltas.Domains[key] = (Saturating(current.Count, row.Count), Math.Max(current.UniquePlayers, row.UniquePlayers));
            }
        }

        /// <summary><c>opened</c>, <c>back</c>, <c>closed</c> or <c>action:&lt;id&gt;</c>.</summary>
        public static bool IsValidStep(string step) =>
            step is "opened" or "back" or "closed"
            || (step.StartsWith("action:", StringComparison.Ordinal) && ActionId.IsMatch(step["action:".Length..]));

        /// <summary>
        /// Outcome of a step: actions carry succeeded/denied/failed; opened/back/closed are always Info
        /// (an omitted outcome means Info for them).
        /// </summary>
        public static bool TryParseOutcome(string? value, string step, out TelemetryOutcome outcome)
        {
            var isAction = step.StartsWith("action:", StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(value))
            {
                outcome = TelemetryOutcome.Info;
                return !isAction;
            }
            if (!Enum.TryParse(value.Trim(), true, out outcome) || int.TryParse(value, out _) || !Enum.IsDefined(outcome))
            {
                return false;
            }
            return isAction ? outcome != TelemetryOutcome.Info : outcome == TelemetryOutcome.Info;
        }

        private static void Reject(WorldAnalyticsBatchResultDto result, string section, int index, string code) =>
            result.Rejected.Add(new WorldAnalyticsRejectionDto { Section = section, Index = index, Code = code });

        private static int Saturating(int a, int b) => (int)Math.Min(int.MaxValue, (long)a + b);

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        private static string Truncate(string? value, int max)
        {
            var trimmed = value?.Trim() ?? "";
            return trimmed.Length <= max ? trimmed : trimmed[..max];
        }
    }
}
