using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Statistics;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace knkwebapi_v2.Repositories
{
    /// <summary>
    /// Player statistics data access (KNG-34). On MySQL the statistic rows are written with
    /// multi-row <c>INSERT … ON DUPLICATE KEY UPDATE</c> (sum: <c>Value + VALUES(Value)</c>, max:
    /// <c>GREATEST</c>); on EF InMemory (tests) the same changes are applied to tracked entities,
    /// like CurrencyRepository.AddTransactionAsync. Keep both paths equivalent — the MySQL path is
    /// exercised only by the KNK_TEST_MYSQL tests (StatisticsUpsertMySqlTests).
    /// </summary>
    public class StatisticsRepository : IStatisticsRepository
    {
        public const string SiegeMatchSourceType = "siege_match";

        // 6 parameters per row; 500 rows stay far below MySQL's 65,535 placeholders.
        private const int UpsertChunk = 500;

        private readonly KnKDbContext _context;

        public StatisticsRepository(KnKDbContext context)
        {
            _context = context;
        }

        private bool IsRelational => _context.Database.IsRelational();

        public async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default)
        {
            if (!IsRelational || _context.Database.CurrentTransaction != null)
            {
                return await work();
            }
            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            var result = await work();
            await transaction.CommitAsync(ct);
            return result;
        }

        public Task SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);

        // ------------------------------------------------------------------ writes

        public async Task ApplyAsync(StatisticsDeltaSet deltas, DateTime now, CancellationToken ct = default)
        {
            await _context.SaveChangesAsync(ct);
            if (deltas.IsEmpty) return;

            if (!IsRelational)
            {
                ApplyTracked(deltas, now);
                await _context.SaveChangesAsync(ct);
                return;
            }

            // Sorted by key so concurrent batches take the row locks in the same order.
            var daily = deltas.Daily.OrderBy(d => d.Key.UserId).ThenBy(d => d.Key.Day)
                .ThenBy(d => d.Key.MetricKey, StringComparer.Ordinal).ThenBy(d => d.Key.ContextKey, StringComparer.Ordinal).ToList();
            await UpsertDailyAsync(daily.Where(d => d.Value.Aggregation == StatisticAggregation.Sum).ToList(), max: false, ct);
            await UpsertDailyAsync(daily.Where(d => d.Value.Aggregation == StatisticAggregation.Max).ToList(), max: true, ct);

            var totals = deltas.Totals.OrderBy(t => t.Key.UserId)
                .ThenBy(t => t.Key.MetricKey, StringComparer.Ordinal).ThenBy(t => t.Key.ContextKey, StringComparer.Ordinal).ToList();
            await UpsertTotalsAsync(totals.Where(t => t.Value.Aggregation == StatisticAggregation.Sum).ToList(), max: false, now, ct);
            await UpsertTotalsAsync(totals.Where(t => t.Value.Aggregation == StatisticAggregation.Max).ToList(), max: true, now, ct);

            var pairs = deltas.KillPairs.OrderBy(p => p.Key.KillerUserId).ThenBy(p => p.Key.VictimUserId)
                .ThenBy(p => p.Key.Day).ThenBy(p => p.Key.ContextKey, StringComparer.Ordinal).ToList();
            await UpsertKillPairsAsync(pairs, ct);
        }

        private void ApplyTracked(StatisticsDeltaSet deltas, DateTime now)
        {
            foreach (var (key, delta) in deltas.Daily)
            {
                var row = _context.PlayerStatDailies.Find(key.UserId, key.Day, key.MetricKey, key.ContextKey);
                if (row == null)
                {
                    _context.PlayerStatDailies.Add(new PlayerStatDaily
                    {
                        UserId = key.UserId, Day = key.Day, MetricKey = key.MetricKey, ContextKey = key.ContextKey,
                        Value = delta.Value, UpdatedAt = delta.At
                    });
                    continue;
                }
                var (value, at, _) = StatisticsDeltaSet.Combine(row.Value, row.UpdatedAt, delta);
                row.Value = value;
                row.UpdatedAt = at;
            }

            foreach (var (key, delta) in deltas.Totals)
            {
                var row = _context.PlayerStatTotals.Find(key.UserId, key.MetricKey, key.ContextKey);
                if (row == null)
                {
                    _context.PlayerStatTotals.Add(new PlayerStatTotal
                    {
                        UserId = key.UserId, MetricKey = key.MetricKey, ContextKey = key.ContextKey,
                        Value = delta.Value, ReachedAt = delta.At, UpdatedAt = now
                    });
                    continue;
                }
                var (value, at, changed) = StatisticsDeltaSet.Combine(row.Value, row.ReachedAt, delta);
                row.Value = value;
                row.ReachedAt = at;
                if (changed) row.UpdatedAt = now;
            }

            foreach (var (key, count) in deltas.KillPairs)
            {
                var row = _context.PlayerPvpKillPairDailies.Find(key.KillerUserId, key.VictimUserId, key.Day, key.ContextKey);
                if (row == null)
                {
                    _context.PlayerPvpKillPairDailies.Add(new PlayerPvpKillPairDaily
                    {
                        KillerUserId = key.KillerUserId, VictimUserId = key.VictimUserId, Day = key.Day,
                        ContextKey = key.ContextKey, Count = count
                    });
                    continue;
                }
                row.Count += count;
            }
        }

        private async Task UpsertDailyAsync(List<KeyValuePair<DailyKey, StatisticDelta>> rows, bool max, CancellationToken ct)
        {
            // MySQL evaluates the assignments left to right and a later one sees the earlier
            // result, so UpdatedAt is computed before Value changes.
            var onDuplicate = max
                ? "`UpdatedAt` = IF(VALUES(`Value`) > `Value`, VALUES(`UpdatedAt`), `UpdatedAt`), `Value` = GREATEST(`Value`, VALUES(`Value`))"
                : "`UpdatedAt` = GREATEST(`UpdatedAt`, VALUES(`UpdatedAt`)), `Value` = `Value` + VALUES(`Value`)";
            foreach (var chunk in rows.Chunk(UpsertChunk))
            {
                var sql = new StringBuilder("INSERT INTO `player_stat_daily` (`UserId`, `Day`, `MetricKey`, `ContextKey`, `Value`, `UpdatedAt`) VALUES ");
                var parameters = new List<object>(chunk.Length * 6);
                for (var i = 0; i < chunk.Length; i++)
                {
                    var (key, delta) = chunk[i];
                    if (i > 0) sql.Append(", ");
                    sql.Append('(').Append(Param(parameters, key.UserId)).Append(", ").Append(Param(parameters, key.Day))
                        .Append(", ").Append(Param(parameters, key.MetricKey)).Append(", ").Append(Param(parameters, key.ContextKey))
                        .Append(", ").Append(Param(parameters, delta.Value)).Append(", ").Append(Param(parameters, delta.At)).Append(')');
                }
                sql.Append(" ON DUPLICATE KEY UPDATE ").Append(onDuplicate);
                await _context.Database.ExecuteSqlRawAsync(sql.ToString(), parameters, ct);
            }
        }

        private async Task UpsertTotalsAsync(List<KeyValuePair<TotalKey, StatisticDelta>> rows, bool max, DateTime now, CancellationToken ct)
        {
            var onDuplicate = max
                ? "`ReachedAt` = IF(VALUES(`Value`) > `Value`, VALUES(`ReachedAt`), `ReachedAt`), "
                  + "`UpdatedAt` = IF(VALUES(`Value`) > `Value`, VALUES(`UpdatedAt`), `UpdatedAt`), "
                  + "`Value` = GREATEST(`Value`, VALUES(`Value`))"
                : "`ReachedAt` = GREATEST(`ReachedAt`, VALUES(`ReachedAt`)), `UpdatedAt` = VALUES(`UpdatedAt`), `Value` = `Value` + VALUES(`Value`)";
            foreach (var chunk in rows.Chunk(UpsertChunk))
            {
                var sql = new StringBuilder("INSERT INTO `player_stat_totals` (`UserId`, `MetricKey`, `ContextKey`, `Value`, `ReachedAt`, `UpdatedAt`) VALUES ");
                var parameters = new List<object>(chunk.Length * 6);
                for (var i = 0; i < chunk.Length; i++)
                {
                    var (key, delta) = chunk[i];
                    if (i > 0) sql.Append(", ");
                    sql.Append('(').Append(Param(parameters, key.UserId)).Append(", ").Append(Param(parameters, key.MetricKey))
                        .Append(", ").Append(Param(parameters, key.ContextKey)).Append(", ").Append(Param(parameters, delta.Value))
                        .Append(", ").Append(Param(parameters, delta.At)).Append(", ").Append(Param(parameters, now)).Append(')');
                }
                sql.Append(" ON DUPLICATE KEY UPDATE ").Append(onDuplicate);
                await _context.Database.ExecuteSqlRawAsync(sql.ToString(), parameters, ct);
            }
        }

        private async Task UpsertKillPairsAsync(List<KeyValuePair<KillPairKey, int>> rows, CancellationToken ct)
        {
            foreach (var chunk in rows.Chunk(UpsertChunk))
            {
                var sql = new StringBuilder("INSERT INTO `player_pvp_kill_pairs_daily` (`KillerUserId`, `VictimUserId`, `Day`, `ContextKey`, `Count`) VALUES ");
                var parameters = new List<object>(chunk.Length * 5);
                for (var i = 0; i < chunk.Length; i++)
                {
                    var (key, count) = chunk[i];
                    if (i > 0) sql.Append(", ");
                    sql.Append('(').Append(Param(parameters, key.KillerUserId)).Append(", ").Append(Param(parameters, key.VictimUserId))
                        .Append(", ").Append(Param(parameters, key.Day)).Append(", ").Append(Param(parameters, key.ContextKey))
                        .Append(", ").Append(Param(parameters, count)).Append(')');
                }
                sql.Append(" ON DUPLICATE KEY UPDATE `Count` = `Count` + VALUES(`Count`)");
                await _context.Database.ExecuteSqlRawAsync(sql.ToString(), parameters, ct);
            }
        }

        private static string Param(List<object> parameters, object value)
        {
            var name = "@p" + parameters.Count;
            parameters.Add(new MySqlParameter(name, value));
            return name;
        }

        // ------------------------------------------------------------------ ingestion

        public Task<bool> BatchExistsAsync(Guid batchId, CancellationToken ct = default) =>
            _context.PlayerStatBatches.AsNoTracking().AnyAsync(b => b.BatchId == batchId, ct);

        public async Task<bool> TryAddBatchAsync(PlayerStatBatch batch, CancellationToken ct = default)
        {
            _context.PlayerStatBatches.Add(batch);
            try
            {
                await _context.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry }
                                               || ex.InnerException is ArgumentException)
            {
                _context.Entry(batch).State = EntityState.Detached;
                return false;
            }
        }

        public async Task<HashSet<int>> GetExistingUserIdsAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0) return new HashSet<int>();
            var found = await _context.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);
            return found.ToHashSet();
        }

        public async Task<Dictionary<Guid, PlayerStatSession>> GetSessionsAsync(IReadOnlyCollection<Guid> sessionKeys, CancellationToken ct = default)
        {
            var keys = sessionKeys.Distinct().ToList();
            if (keys.Count == 0) return new Dictionary<Guid, PlayerStatSession>();
            var sessions = await _context.PlayerStatSessions.Where(s => keys.Contains(s.SessionKey)).ToListAsync(ct);
            return sessions.ToDictionary(s => s.SessionKey);
        }

        public void AddSession(PlayerStatSession session) => _context.PlayerStatSessions.Add(session);

        public async Task<Dictionary<int, PlayerStatProfile>> GetProfilesForUpdateAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, PlayerStatProfile>();
            var profiles = await _context.PlayerStatProfiles.Where(p => ids.Contains(p.UserId)).ToListAsync(ct);
            return profiles.ToDictionary(p => p.UserId);
        }

        public void AddProfile(PlayerStatProfile profile) => _context.PlayerStatProfiles.Add(profile);

        // ------------------------------------------------------------------ reads

        public Task<User?> GetUserAsync(int userId, CancellationToken ct = default) =>
            _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);

        public Task<List<User>> GetUsersAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default)
        {
            var ids = userIds.Distinct().ToList();
            return _context.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(ct);
        }

        public Task<List<StatisticValueRow>> GetTotalsAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default)
        {
            var ids = userIds.Distinct().ToList();
            return _context.PlayerStatTotals.AsNoTracking()
                .Where(t => ids.Contains(t.UserId))
                .Select(t => new StatisticValueRow(t.UserId, t.MetricKey, t.ContextKey, t.Value, null))
                .ToListAsync(ct);
        }

        public Task<List<StatisticValueRow>> GetDailyAsync(IReadOnlyCollection<int> userIds, DateOnly from, DateOnly toExclusive,
            string? metricKey = null, CancellationToken ct = default)
        {
            var ids = userIds.Distinct().ToList();
            var query = _context.PlayerStatDailies.AsNoTracking()
                .Where(d => ids.Contains(d.UserId) && d.Day >= from && d.Day < toExclusive);
            if (metricKey != null)
            {
                query = query.Where(d => d.MetricKey == metricKey);
            }
            return query.Select(d => new StatisticValueRow(d.UserId, d.MetricKey, d.ContextKey, d.Value, d.Day)).ToListAsync(ct);
        }

        public Task<List<PlayerStatProfile>> GetProfilesAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default)
        {
            var ids = userIds.Distinct().ToList();
            return _context.PlayerStatProfiles.AsNoTracking().Where(p => ids.Contains(p.UserId)).ToListAsync(ct);
        }

        public async Task<(List<PlayerTitleChange> Items, int Total)> GetTitleChangesAsync(IReadOnlyCollection<int> userIds, int skip, int take,
            CancellationToken ct = default)
        {
            var ids = userIds.Distinct().ToList();
            var query = _context.PlayerTitleChanges.AsNoTracking().Where(c => ids.Contains(c.UserId));
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(c => c.ChangedAt).ThenByDescending(c => c.Id).Skip(skip).Take(take).ToListAsync(ct);
            return (items, total);
        }

        public Task<List<UserDomainDiscovery>> GetDiscoveriesAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default)
        {
            var ids = userIds.Distinct().ToList();
            return _context.UserDomainDiscoveries.AsNoTracking().Where(d => ids.Contains(d.UserId)).ToListAsync(ct);
        }

        // ------------------------------------------------------------------ visibility

        public Task<List<PlayerStatVisibility>> GetVisibilityAsync(int userId, bool tracked = false, CancellationToken ct = default)
        {
            var query = _context.PlayerStatVisibilities.Where(v => v.UserId == userId);
            return (tracked ? query : query.AsNoTracking()).ToListAsync(ct);
        }

        public void AddVisibility(PlayerStatVisibility row) => _context.PlayerStatVisibilities.Add(row);

        public async Task LockUserAsync(int userId, CancellationToken ct = default)
        {
            if (!IsRelational) return;
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT `Id` FROM `users` WHERE `Id` = {userId} FOR UPDATE", ct);
        }

        // ------------------------------------------------------------------ projection

        public async Task<StatisticsProjectionCursor> GetOrCreateCursorAsync(string name, CancellationToken ct = default)
        {
            var cursor = await _context.StatisticsProjectionCursors.FirstOrDefaultAsync(c => c.Name == name, ct);
            if (cursor != null) return cursor;
            cursor = new StatisticsProjectionCursor { Name = name, LastSourceId = 0, UpdatedAt = DateTime.UtcNow };
            _context.StatisticsProjectionCursors.Add(cursor);
            return cursor;
        }

        public Task<List<LedgerLegRow>> GetLedgerLegsAsync(long afterEntryId, int take, int? userId = null, long? maxEntryId = null,
            CancellationToken ct = default)
        {
            var query = _context.CurrencyEntries.AsNoTracking()
                .Where(e => e.Id > afterEntryId && e.AccountKind == CurrencyAccountKind.User && e.UserId != null);
            if (userId != null)
            {
                query = query.Where(e => e.UserId == userId);
            }
            if (maxEntryId != null)
            {
                query = query.Where(e => e.Id <= maxEntryId);
            }
            return query.OrderBy(e => e.Id).Take(take)
                .Select(e => new LedgerLegRow(e.Id, e.TransactionId, e.UserId!.Value, e.Currency, e.Amount,
                    e.BalanceBefore ?? 0, e.BalanceAfter ?? 0, e.Transaction.ReasonCode, e.Transaction.CreatedAt,
                    e.Transaction.ReversesTransactionId))
                .ToListAsync(ct);
        }

        public async Task<Dictionary<long, (string ReasonCode, long? ReversesTransactionId)>> GetTransactionReasonsAsync(
            IReadOnlyCollection<long> transactionIds, CancellationToken ct = default)
        {
            var ids = transactionIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<long, (string, long?)>();
            var rows = await _context.CurrencyTransactions.AsNoTracking()
                .Where(t => ids.Contains(t.Id))
                .Select(t => new { t.Id, t.ReasonCode, t.ReversesTransactionId })
                .ToListAsync(ct);
            return rows.ToDictionary(r => r.Id, r => (r.ReasonCode, r.ReversesTransactionId));
        }

        public Task<List<TitleBracket>> GetTitleBracketsAsync(CancellationToken ct = default) =>
            _context.TitleBrackets.AsNoTracking().OrderBy(b => b.MinExperience).ThenBy(b => b.Id).ToListAsync(ct);

        public async Task<HashSet<long>> GetExistingTitleChangeEntryIdsAsync(IReadOnlyCollection<long> entryIds, CancellationToken ct = default)
        {
            var ids = entryIds.Distinct().ToList();
            if (ids.Count == 0) return new HashSet<long>();
            var found = await _context.PlayerTitleChanges.AsNoTracking().Where(c => ids.Contains(c.CurrencyEntryId))
                .Select(c => c.CurrencyEntryId).ToListAsync(ct);
            return found.ToHashSet();
        }

        public void AddTitleChanges(IEnumerable<PlayerTitleChange> changes) => _context.PlayerTitleChanges.AddRange(changes);

        public Task<List<SiegeMatch>> GetUnprojectedSiegeMatchesAsync(int take, CancellationToken ct = default) =>
            _context.SiegeMatches.AsNoTracking()
                .Where(m => (m.Status == SiegeMatchStatus.Completed || m.Status == SiegeMatchStatus.Aborted) && m.EndedAt != null)
                .Where(m => !_context.StatisticsProjectedSources.Any(p => p.SourceType == SiegeMatchSourceType && p.SourceId == m.Id))
                .OrderBy(m => m.Id)
                .Take(take)
                .Include(m => m.Participants).ThenInclude(p => p.SiegeTeam)
                .AsSplitQuery()
                .ToListAsync(ct);

        public Task<List<SiegeMatch>> GetProjectedSiegeMatchesOfUserAsync(int userId, CancellationToken ct = default) =>
            _context.SiegeMatches.AsNoTracking()
                .Where(m => m.Participants.Any(p => p.UserId == userId))
                .Where(m => _context.StatisticsProjectedSources.Any(p => p.SourceType == SiegeMatchSourceType && p.SourceId == m.Id))
                .OrderBy(m => m.Id)
                .Include(m => m.Participants).ThenInclude(p => p.SiegeTeam)
                .AsSplitQuery()
                .ToListAsync(ct);

        public void AddProjectedSources(IEnumerable<StatisticsProjectedSource> sources) =>
            _context.StatisticsProjectedSources.AddRange(sources);

        public async Task<int> DeleteMetricRowsAsync(IReadOnlyCollection<string> metricKeys, string? contextKey, int? userId,
            CancellationToken ct = default)
        {
            var keys = metricKeys.Distinct().ToList();
            var daily = _context.PlayerStatDailies.Where(d => keys.Contains(d.MetricKey));
            var totals = _context.PlayerStatTotals.Where(t => keys.Contains(t.MetricKey));
            if (contextKey != null)
            {
                daily = daily.Where(d => d.ContextKey == contextKey);
                totals = totals.Where(t => t.ContextKey == contextKey);
            }
            if (userId != null)
            {
                daily = daily.Where(d => d.UserId == userId);
                totals = totals.Where(t => t.UserId == userId);
            }
            return await DeleteAsync(daily, ct) + await DeleteAsync(totals, ct);
        }

        public Task<int> DeleteTitleChangesAsync(int? userId, CancellationToken ct = default) =>
            DeleteAsync(userId == null ? _context.PlayerTitleChanges : _context.PlayerTitleChanges.Where(c => c.UserId == userId), ct);

        public Task<int> DeleteProjectedSourcesAsync(string sourceType, CancellationToken ct = default) =>
            DeleteAsync(_context.StatisticsProjectedSources.Where(s => s.SourceType == sourceType), ct);

        // ------------------------------------------------------------------ jobs

        public async Task<int> CloseTimedOutSessionsAsync(DateTime heartbeatBefore, CancellationToken ct = default)
        {
            var open = _context.PlayerStatSessions.Where(s => s.EndedAt == null && s.LastHeartbeatAt < heartbeatBefore);
            if (IsRelational)
            {
                return await open.ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.EndedAt, s => s.LastHeartbeatAt)
                    .SetProperty(s => s.EndReason, PlayerSessionEndReason.Timeout), ct);
            }
            var sessions = await open.ToListAsync(ct);
            foreach (var session in sessions)
            {
                session.EndedAt = session.LastHeartbeatAt;
                session.EndReason = PlayerSessionEndReason.Timeout;
            }
            await _context.SaveChangesAsync(ct);
            return sessions.Count;
        }

        public Task<int> PurgeDailyBeforeAsync(DateOnly day, CancellationToken ct = default) =>
            DeleteAsync(_context.PlayerStatDailies.Where(d => d.Day < day), ct);

        public Task<int> PurgeSessionsStartedBeforeAsync(DateTime before, CancellationToken ct = default) =>
            DeleteAsync(_context.PlayerStatSessions.Where(s => s.EndedAt != null && s.StartedAt < before), ct);

        public Task<int> PurgeBatchesBeforeAsync(DateTime before, CancellationToken ct = default) =>
            DeleteAsync(_context.PlayerStatBatches.Where(b => b.ReceivedAt < before), ct);

        public Task<int> PurgeKillPairsBeforeAsync(DateOnly day, CancellationToken ct = default) =>
            DeleteAsync(_context.PlayerPvpKillPairDailies.Where(p => p.Day < day), ct);

        private async Task<int> DeleteAsync<T>(IQueryable<T> query, CancellationToken ct) where T : class
        {
            if (IsRelational)
            {
                return await query.ExecuteDeleteAsync(ct);
            }
            // EF InMemory has no ExecuteDelete.
            var rows = await query.ToListAsync(ct);
            _context.RemoveRange(rows);
            await _context.SaveChangesAsync(ct);
            return rows.Count;
        }
    }
}
