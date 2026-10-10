using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    /// <summary>
    /// Diagnostic telemetry data access (KNG-34 link 6). Reads the ledger and Siege tables for the
    /// timeline but never writes them. Bulk deletes use ExecuteDelete on MySQL and fall back to
    /// tracked removal on EF InMemory (tests), like <see cref="StatisticsRepository"/>.
    /// </summary>
    public class TelemetryRepository : ITelemetryRepository
    {
        private readonly KnKDbContext _context;

        public TelemetryRepository(KnKDbContext context)
        {
            _context = context;
        }

        private bool IsRelational => _context.Database.IsRelational();

        public async Task<HashSet<Guid>> GetExistingEventIdsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken ct = default)
        {
            if (eventIds.Count == 0) return new HashSet<Guid>();
            var ids = eventIds.ToList();
            var found = await _context.TelemetryEvents.AsNoTracking()
                .Where(e => ids.Contains(e.EventId))
                .Select(e => e.EventId)
                .ToListAsync(ct);
            return found.ToHashSet();
        }

        public async Task<int> InsertEventsAsync(IReadOnlyCollection<TelemetryEvent> events, CancellationToken ct = default)
        {
            if (events.Count == 0) return 0;
            // One writer per API instance; a second instance racing on the same id is caught by the
            // unique index and the batch is retried row by row by the writer.
            var existing = await GetExistingEventIdsAsync(events.Select(e => e.EventId).ToList(), ct);
            var fresh = events
                .Where(e => !existing.Contains(e.EventId))
                .GroupBy(e => e.EventId)
                .Select(g => g.First())
                .ToList();
            if (fresh.Count == 0) return 0;
            _context.TelemetryEvents.AddRange(fresh);
            try
            {
                await _context.SaveChangesAsync(ct);
            }
            finally
            {
                foreach (var e in fresh) _context.Entry(e).State = EntityState.Detached;
            }
            return fresh.Count;
        }

        public async Task<List<TelemetryEvent>> SearchAsync(TelemetrySearchFilter filter, TelemetryCursor? before, int take,
            CancellationToken ct = default)
        {
            var query = _context.TelemetryEvents.AsNoTracking().AsQueryable();
            if (filter.UserId != null) query = query.Where(e => e.UserId == filter.UserId);
            if (filter.From != null) query = query.Where(e => e.OccurredAt >= filter.From);
            if (filter.To != null) query = query.Where(e => e.OccurredAt < filter.To);
            if (filter.SessionKey != null) query = query.Where(e => e.SessionKey == filter.SessionKey);
            if (filter.TestRunId != null) query = query.Where(e => e.TestRunId == filter.TestRunId);
            if (filter.MatchId != null) query = query.Where(e => e.MatchId == filter.MatchId);
            if (filter.CorrelationId != null) query = query.Where(e => e.CorrelationId == filter.CorrelationId);
            if (filter.Name != null) query = query.Where(e => e.Name == filter.Name);
            if (filter.Outcome != null) query = query.Where(e => e.Outcome == filter.Outcome);
            if (before != null)
            {
                query = query.Where(e => e.OccurredAt < before.OccurredAt
                                         || (e.OccurredAt == before.OccurredAt && e.Id < before.Id));
            }
            return await query
                .OrderByDescending(e => e.OccurredAt)
                .ThenByDescending(e => e.Id)
                .Take(take)
                .ToListAsync(ct);
        }

        public Task<List<TelemetryEvent>> GetUserEventsAsync(int userId, DateTime from, DateTime to, int take, CancellationToken ct = default) =>
            _context.TelemetryEvents.AsNoTracking()
                .Where(e => e.UserId == userId && e.OccurredAt >= from && e.OccurredAt < to)
                .OrderBy(e => e.OccurredAt)
                .ThenBy(e => e.ServerSeq)
                .ThenBy(e => e.Id)
                .Take(take)
                .ToListAsync(ct);

        public Task<TelemetryEvent?> GetEventAsync(Guid eventId, CancellationToken ct = default) =>
            _context.TelemetryEvents.AsNoTracking().FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        public Task<List<TelemetryEvent>> GetByCorrelationAsync(string correlationId, long excludeId, int take, CancellationToken ct = default) =>
            _context.TelemetryEvents.AsNoTracking()
                .Where(e => e.CorrelationId == correlationId && e.Id != excludeId)
                .OrderBy(e => e.OccurredAt)
                .ThenBy(e => e.ServerSeq)
                .ThenBy(e => e.Id)
                .Take(take)
                .ToListAsync(ct);

        public Task<List<string>> GetLedgerPublicIdsByCorrelationAsync(string correlationId, int take, CancellationToken ct = default) =>
            _context.CurrencyTransactions.AsNoTracking()
                .Where(t => t.CorrelationId == correlationId)
                .OrderBy(t => t.Id)
                .Select(t => t.PublicId)
                .Take(take)
                .ToListAsync(ct);

        public async Task<List<TelemetryLedgerLegRow>> GetLedgerLegsAsync(int userId, DateTime from, DateTime to, int take,
            CancellationToken ct = default)
        {
            var rows = await _context.CurrencyEntries.AsNoTracking()
                .Where(e => e.UserId == userId && e.Transaction.CreatedAt >= from && e.Transaction.CreatedAt < to)
                .OrderBy(e => e.Transaction.CreatedAt)
                .ThenBy(e => e.Id)
                .Take(take)
                .Select(e => new
                {
                    e.Transaction.PublicId,
                    e.Transaction.ReasonCode,
                    e.Transaction.CorrelationId,
                    e.Transaction.CreatedAt,
                    e.Currency,
                    e.Operation,
                    e.Amount,
                    e.BalanceBefore,
                    e.BalanceAfter
                })
                .ToListAsync(ct);
            return rows.Select(r => new TelemetryLedgerLegRow(r.PublicId, r.ReasonCode, r.CorrelationId, r.CreatedAt, r.Currency,
                    r.BalanceBefore != null && r.BalanceAfter != null
                        ? r.BalanceAfter.Value - r.BalanceBefore.Value
                        : r.Operation == CurrencyOperation.Remove ? -r.Amount : r.Amount))
                .ToList();
        }

        public async Task<List<TelemetrySiegeRow>> GetSiegeParticipationsAsync(int userId, DateTime from, DateTime to, int take,
            CancellationToken ct = default)
        {
            // A participation overlaps the window when it started before its end and ended (left,
            // match end, or still running) after its start.
            var rows = await _context.SiegeMatchParticipants.AsNoTracking()
                .Where(p => p.UserId == userId && p.JoinedAt < to
                            && (p.LeftAt ?? p.SiegeMatch.EndedAt ?? DateTime.MaxValue) >= from)
                .OrderBy(p => p.JoinedAt)
                .Take(take)
                .Select(p => new
                {
                    p.SiegeMatchId,
                    p.SiegeMatch.Status,
                    p.JoinedAt,
                    p.LeftAt,
                    p.SiegeMatch.EndedAt,
                    p.SiegeTeamId,
                    p.Kills,
                    p.Deaths,
                    p.Captures
                })
                .ToListAsync(ct);
            return rows.Select(r => new TelemetrySiegeRow(r.SiegeMatchId, r.Status, r.JoinedAt, r.LeftAt, r.EndedAt,
                r.SiegeTeamId, r.Kills, r.Deaths, r.Captures)).ToList();
        }

        public Task<int> CountSinceAsync(DateTime since, CancellationToken ct = default) =>
            _context.TelemetryEvents.AsNoTracking().CountAsync(e => e.OccurredAt >= since, ct);

        public async Task<Dictionary<int, string>> GetUsernamesAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default)
        {
            if (userIds.Count == 0) return new Dictionary<int, string>();
            var ids = userIds.Distinct().ToList();
            return await _context.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        }

        public Task<bool> UserExistsAsync(int userId, CancellationToken ct = default) =>
            _context.Users.AsNoTracking().AnyAsync(u => u.Id == userId, ct);

        public Task<List<TelemetryTestRun>> GetTestRunsAsync(CancellationToken ct = default) =>
            _context.TelemetryTestRuns.AsNoTracking().OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).ToListAsync(ct);

        public Task<TelemetryTestRun?> GetTestRunAsync(int id, CancellationToken ct = default) =>
            _context.TelemetryTestRuns.FirstOrDefaultAsync(r => r.Id == id, ct);

        public async Task AddTestRunAsync(TelemetryTestRun run, CancellationToken ct = default)
        {
            _context.TelemetryTestRuns.Add(run);
            await _context.SaveChangesAsync(ct);
        }

        public Task<List<TelemetryEnhancedTarget>> GetActiveTargetsAsync(DateTime now, CancellationToken ct = default) =>
            _context.TelemetryEnhancedTargets.AsNoTracking()
                .Where(t => t.ExpiresAt > now)
                .OrderBy(t => t.ExpiresAt)
                .ToListAsync(ct);

        public Task<TelemetryEnhancedTarget?> GetTargetAsync(int id, CancellationToken ct = default) =>
            _context.TelemetryEnhancedTargets.FirstOrDefaultAsync(t => t.Id == id, ct);

        public async Task AddTargetAsync(TelemetryEnhancedTarget target, CancellationToken ct = default)
        {
            _context.TelemetryEnhancedTargets.Add(target);
            await _context.SaveChangesAsync(ct);
        }

        public async Task RemoveTargetAsync(TelemetryEnhancedTarget target, CancellationToken ct = default)
        {
            _context.TelemetryEnhancedTargets.Remove(target);
            await _context.SaveChangesAsync(ct);
        }

        public Task SaveChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);

        public Task<int> PurgeEventsBeforeAsync(TelemetryLevel level, DateTime before, CancellationToken ct = default) =>
            DeleteAsync(_context.TelemetryEvents.Where(e => e.Level == level && e.OccurredAt < before), ct);

        public Task<int> PurgeTargetsExpiredBeforeAsync(DateTime before, CancellationToken ct = default) =>
            DeleteAsync(_context.TelemetryEnhancedTargets.Where(t => t.ExpiresAt < before), ct);

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
