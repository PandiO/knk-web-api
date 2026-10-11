using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Statistics;

namespace knkwebapi_v2.Repositories.Interfaces
{
    /// <summary>A user leg of the currency ledger with its transaction's facts, for the ledger projector.</summary>
    public sealed record LedgerLegRow(
        long EntryId,
        long TransactionId,
        int UserId,
        Currency Currency,
        long Amount,
        long BalanceBefore,
        long BalanceAfter,
        string ReasonCode,
        DateTime CreatedAt,
        long? ReversesTransactionId);

    /// <summary>A stored statistic value (daily row or lifetime total) of one user.</summary>
    public sealed record StatisticValueRow(int UserId, string MetricKey, string ContextKey, decimal Value, DateOnly? Day);

    /// <summary>
    /// Player statistics data access (KNG-34, IMPLEMENTATION_PLAN.md §1.1, §3.1). Writes go through
    /// <see cref="ApplyAsync"/> (multi-row MySQL upserts; tracked-entity updates on EF InMemory).
    /// Reads the ledger and Siege tables but never writes them.
    /// </summary>
    public interface IStatisticsRepository
    {
        /// <summary>Runs <paramref name="work"/> in one database transaction (ReadCommitted), or
        /// joins the current one; on EF InMemory just runs it.</summary>
        Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default);

        Task SaveChangesAsync(CancellationToken ct = default);

        /// <summary>Applies pre-aggregated daily, total and kill-pair changes.</summary>
        Task ApplyAsync(StatisticsDeltaSet deltas, DateTime now, CancellationToken ct = default);

        // ---- Ingestion ----

        Task<bool> BatchExistsAsync(Guid batchId, CancellationToken ct = default);

        /// <summary>Inserts the batch row; false when another request inserted the same id first.</summary>
        Task<bool> TryAddBatchAsync(PlayerStatBatch batch, CancellationToken ct = default);

        Task<HashSet<int>> GetExistingUserIdsAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default);

        /// <summary>Tracked sessions by key.</summary>
        Task<Dictionary<Guid, PlayerStatSession>> GetSessionsAsync(IReadOnlyCollection<Guid> sessionKeys, CancellationToken ct = default);

        void AddSession(PlayerStatSession session);

        /// <summary>Tracked profiles by user id.</summary>
        Task<Dictionary<int, PlayerStatProfile>> GetProfilesForUpdateAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default);

        void AddProfile(PlayerStatProfile profile);

        // ---- Reads ----

        Task<User?> GetUserAsync(int userId, CancellationToken ct = default);

        /// <summary>The user with this username, ignoring case.</summary>
        Task<User?> GetUserByUsernameAsync(string username, CancellationToken ct = default);

        Task<List<User>> GetUsersAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default);

        Task<List<StatisticValueRow>> GetTotalsAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default);

        Task<List<StatisticValueRow>> GetDailyAsync(IReadOnlyCollection<int> userIds, DateOnly from, DateOnly toExclusive,
            string? metricKey = null, CancellationToken ct = default);

        Task<List<PlayerStatProfile>> GetProfilesAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default);

        Task<(List<PlayerTitleChange> Items, int Total)> GetTitleChangesAsync(IReadOnlyCollection<int> userIds, int skip, int take,
            CancellationToken ct = default);

        Task<List<UserDomainDiscovery>> GetDiscoveriesAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default);

        /// <summary>Stored kill counts of the given pairs (missing pairs are absent).</summary>
        Task<Dictionary<KillPairKey, int>> GetKillPairCountsAsync(IReadOnlyCollection<KillPairKey> pairs, CancellationToken ct = default);

        // ---- Visibility ----

        Task<List<PlayerStatVisibility>> GetVisibilityAsync(int userId, bool tracked = false, CancellationToken ct = default);

        void AddVisibility(PlayerStatVisibility row);

        /// <summary>Locks the user's row (SELECT … FOR UPDATE) for the rest of the transaction, so
        /// two visibility updates of one player are serialised. No-op on EF InMemory.</summary>
        Task LockUserAsync(int userId, CancellationToken ct = default);

        // ---- Projection ----

        /// <summary>The tracked cursor (created when missing); locked FOR UPDATE on MySQL until the
        /// transaction ends.</summary>
        Task<StatisticsProjectionCursor> GetOrCreateCursorAsync(string name, CancellationToken ct = default);

        /// <summary>User legs with Id &gt; <paramref name="afterEntryId"/>, ascending, at most
        /// <paramref name="take"/>; optionally of one user and up to an entry id.</summary>
        Task<List<LedgerLegRow>> GetLedgerLegsAsync(long afterEntryId, int take, int? userId = null, long? maxEntryId = null,
            CancellationToken ct = default);

        /// <summary>Reason code and reversed transaction id per transaction id.</summary>
        Task<Dictionary<long, (string ReasonCode, long? ReversesTransactionId)>> GetTransactionReasonsAsync(
            IReadOnlyCollection<long> transactionIds, CancellationToken ct = default);

        Task<List<TitleBracket>> GetTitleBracketsAsync(CancellationToken ct = default);

        Task<HashSet<long>> GetExistingTitleChangeEntryIdsAsync(IReadOnlyCollection<long> entryIds, CancellationToken ct = default);

        void AddTitleChanges(IEnumerable<PlayerTitleChange> changes);

        /// <summary>Completed or aborted matches with an end time that have no projected-source row.</summary>
        Task<List<SiegeMatch>> GetUnprojectedSiegeMatchesAsync(int take, CancellationToken ct = default);

        Task<List<SiegeMatch>> GetProjectedSiegeMatchesOfUserAsync(int userId, CancellationToken ct = default);

        void AddProjectedSources(IEnumerable<StatisticsProjectedSource> sources);

        /// <summary>Deletes stored rows (daily + totals) of the given metric keys, optionally only in one
        /// context and/or of one user. Returns rows deleted.</summary>
        Task<int> DeleteMetricRowsAsync(IReadOnlyCollection<string> metricKeys, string? contextKey, int? userId, CancellationToken ct = default);

        Task<int> DeleteTitleChangesAsync(int? userId, CancellationToken ct = default);

        Task<int> DeleteProjectedSourcesAsync(string sourceType, CancellationToken ct = default);

        // ---- Jobs ----

        /// <summary>Closes open sessions whose last heartbeat is before <paramref name="heartbeatBefore"/>
        /// (EndedAt = LastHeartbeatAt, EndReason = Timeout). Returns sessions closed.</summary>
        Task<int> CloseTimedOutSessionsAsync(DateTime heartbeatBefore, CancellationToken ct = default);

        Task<int> PurgeDailyBeforeAsync(DateOnly day, CancellationToken ct = default);

        Task<int> PurgeSessionsStartedBeforeAsync(DateTime before, CancellationToken ct = default);

        Task<int> PurgeBatchesBeforeAsync(DateTime before, CancellationToken ct = default);

        Task<int> PurgeKillPairsBeforeAsync(DateOnly day, CancellationToken ct = default);
    }
}
