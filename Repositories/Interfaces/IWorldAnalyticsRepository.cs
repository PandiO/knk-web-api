using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    /// <summary>Key of a movement-cell row within one day.</summary>
    public readonly record struct MovementCellKey(string World, short CellSize, int CellX, int CellZ);

    /// <summary>Key of a menu-funnel row within one day.</summary>
    public readonly record struct MenuStepKey(string MenuKey, string Step, TelemetryOutcome Outcome);

    /// <summary>Key of a domain-interaction row within one day.</summary>
    public readonly record struct DomainInteractionKey(int DomainId, string Kind);

    /// <summary>One batch's pre-aggregated rows, all on one local day.</summary>
    public sealed class WorldAnalyticsDeltas
    {
        public Dictionary<MovementCellKey, int> Cells { get; } = new();
        public Dictionary<MenuStepKey, int> MenuSteps { get; } = new();
        public Dictionary<DomainInteractionKey, (int Count, int UniquePlayers)> Domains { get; } = new();

        public bool IsEmpty => Cells.Count == 0 && MenuSteps.Count == 0 && Domains.Count == 0;
    }

    public sealed record MovementCellSum(short CellSize, int CellX, int CellZ, long Samples);

    public sealed record MovementWorldSum(string World, short CellSize, long Samples);

    public sealed record MenuStepSum(string MenuKey, string Step, TelemetryOutcome Outcome, long Count);

    public sealed record DomainLabel(int Id, string Name, string WgRegionId);

    /// <summary>World analytics tables (KNG-34 link 7, IMPLEMENTATION_PLAN.md §1.4).</summary>
    public interface IWorldAnalyticsRepository
    {
        Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default);

        Task<bool> BatchExistsAsync(Guid batchId, CancellationToken ct = default);

        /// <summary>False when a concurrent request inserted the same batch id first.</summary>
        Task<bool> TryAddBatchAsync(WorldAnalyticsBatch batch, CancellationToken ct = default);

        /// <summary>Domain ids by WorldGuard region id (case-insensitive; unknown regions are absent).</summary>
        Task<Dictionary<string, int>> ResolveDomainIdsAsync(IReadOnlyCollection<string> regionIds, CancellationToken ct = default);

        /// <summary>Adds the counts to the day's rows (samples/counts summed, unique players = max).</summary>
        Task ApplyAsync(DateOnly day, WorldAnalyticsDeltas deltas, CancellationToken ct = default);

        Task<List<MovementWorldSum>> GetMovementWorldsAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

        Task<List<MovementCellSum>> GetMovementCellsAsync(string world, DateOnly from, DateOnly to, CancellationToken ct = default);

        Task<List<MenuStepSum>> GetMenuStepsAsync(string? menuKey, DateOnly from, DateOnly to, CancellationToken ct = default);

        Task<List<DomainInteractionDaily>> GetDomainRowsAsync(string? kind, DateOnly from, DateOnly to, CancellationToken ct = default);

        Task<Dictionary<int, DomainLabel>> GetDomainLabelsAsync(IReadOnlyCollection<int> domainIds, CancellationToken ct = default);

        /// <summary>Deletes cell, funnel and domain rows of days before <paramref name="day"/>.</summary>
        Task<int> PurgeDailyBeforeAsync(DateOnly day, CancellationToken ct = default);

        Task<int> PurgeBatchesBeforeAsync(DateTime before, CancellationToken ct = default);
    }
}
