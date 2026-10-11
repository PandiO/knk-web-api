using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    /// <summary>Filters of an owner event search (all optional; window on OccurredAt).</summary>
    public sealed record TelemetrySearchFilter(
        int? UserId = null,
        DateTime? From = null,
        DateTime? To = null,
        Guid? SessionKey = null,
        int? TestRunId = null,
        int? MatchId = null,
        string? CorrelationId = null,
        string? Name = null,
        TelemetryOutcome? Outcome = null);

    /// <summary>Keyset cursor: events strictly older than (OccurredAt, Id).</summary>
    public sealed record TelemetryCursor(DateTime OccurredAt, long Id);

    /// <summary>A ledger leg of one user for the diagnostic timeline.</summary>
    public sealed record TelemetryLedgerLegRow(
        string PublicId, string ReasonCode, string? CorrelationId, DateTime CreatedAt, Currency Currency, long Delta);

    /// <summary>A Siege participation of one user for the diagnostic timeline.</summary>
    public sealed record TelemetrySiegeRow(
        int MatchId, SiegeMatchStatus Status, DateTime JoinedAt, DateTime? LeftAt, DateTime? EndedAt,
        int? TeamId, int Kills, int Deaths, int Captures);

    /// <summary>Diagnostic telemetry data access (KNG-34 link 6, IMPLEMENTATION_PLAN.md §1.3, §3.3).</summary>
    public interface ITelemetryRepository
    {
        Task<HashSet<Guid>> GetExistingEventIdsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken ct = default);

        /// <summary>Inserts the events whose ids are not stored yet; returns the number inserted.</summary>
        Task<int> InsertEventsAsync(IReadOnlyCollection<TelemetryEvent> events, CancellationToken ct = default);

        /// <summary>Newest first (OccurredAt desc, Id desc), older than the cursor when given.</summary>
        Task<List<TelemetryEvent>> SearchAsync(TelemetrySearchFilter filter, TelemetryCursor? before, int take, CancellationToken ct = default);

        /// <summary>Oldest first within the window.</summary>
        Task<List<TelemetryEvent>> GetUserEventsAsync(int userId, DateTime from, DateTime to, int take, CancellationToken ct = default);

        Task<TelemetryEvent?> GetEventAsync(Guid eventId, CancellationToken ct = default);

        Task<List<TelemetryEvent>> GetByCorrelationAsync(string correlationId, long excludeId, int take, CancellationToken ct = default);

        Task<List<string>> GetLedgerPublicIdsByCorrelationAsync(string correlationId, int take, CancellationToken ct = default);

        Task<List<TelemetryLedgerLegRow>> GetLedgerLegsAsync(int userId, DateTime from, DateTime to, int take, CancellationToken ct = default);

        Task<List<TelemetrySiegeRow>> GetSiegeParticipationsAsync(int userId, DateTime from, DateTime to, int take, CancellationToken ct = default);

        Task<int> CountSinceAsync(DateTime since, CancellationToken ct = default);

        Task<Dictionary<int, string>> GetUsernamesAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default);

        Task<bool> UserExistsAsync(int userId, CancellationToken ct = default);

        Task<List<TelemetryTestRun>> GetTestRunsAsync(CancellationToken ct = default);

        Task<TelemetryTestRun?> GetTestRunAsync(int id, CancellationToken ct = default);

        Task AddTestRunAsync(TelemetryTestRun run, CancellationToken ct = default);

        /// <summary>Targets whose ExpiresAt is after <paramref name="now"/>.</summary>
        Task<List<TelemetryEnhancedTarget>> GetActiveTargetsAsync(DateTime now, CancellationToken ct = default);

        Task<TelemetryEnhancedTarget?> GetTargetAsync(int id, CancellationToken ct = default);

        Task AddTargetAsync(TelemetryEnhancedTarget target, CancellationToken ct = default);

        Task RemoveTargetAsync(TelemetryEnhancedTarget target, CancellationToken ct = default);

        Task SaveChangesAsync(CancellationToken ct = default);

        Task<int> PurgeEventsBeforeAsync(TelemetryLevel level, DateTime before, CancellationToken ct = default);

        Task<int> PurgeTargetsExpiredBeforeAsync(DateTime before, CancellationToken ct = default);
    }
}
