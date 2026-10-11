using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace knkwebapi_v2.Repositories
{
    /// <summary>
    /// World analytics data access (KNG-34 link 7). Like <see cref="StatisticsRepository"/>: on MySQL
    /// rows are written with multi-row <c>INSERT … ON DUPLICATE KEY UPDATE</c>; on EF InMemory (tests)
    /// the same changes go through tracked entities. Keep both paths equivalent — the MySQL path is
    /// exercised by WorldAnalyticsMySqlTests (KNK_TEST_MYSQL).
    /// </summary>
    public class WorldAnalyticsRepository : IWorldAnalyticsRepository
    {
        // ≤ 6 parameters per row; 500 rows stay far below MySQL's 65,535 placeholders.
        private const int UpsertChunk = 500;

        private readonly KnKDbContext _context;

        public WorldAnalyticsRepository(KnKDbContext context)
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

        public Task<bool> BatchExistsAsync(Guid batchId, CancellationToken ct = default) =>
            _context.WorldAnalyticsBatches.AsNoTracking().AnyAsync(b => b.BatchId == batchId, ct);

        public async Task<bool> TryAddBatchAsync(WorldAnalyticsBatch batch, CancellationToken ct = default)
        {
            _context.WorldAnalyticsBatches.Add(batch);
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

        public async Task<Dictionary<string, int>> ResolveDomainIdsAsync(IReadOnlyCollection<string> regionIds, CancellationToken ct = default)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (regionIds.Count == 0) return result;
            var lowered = regionIds.Select(r => r.ToLowerInvariant()).Distinct().ToList();
            var rows = await _context.Domains.AsNoTracking()
                .Where(d => lowered.Contains(d.WgRegionId.ToLower()))
                .Select(d => new { d.Id, d.WgRegionId })
                .ToListAsync(ct);
            // Lowest id wins if two domains share a region id (they shouldn't).
            foreach (var row in rows.OrderBy(r => r.Id))
            {
                result.TryAdd(row.WgRegionId, row.Id);
            }
            return result;
        }

        // ------------------------------------------------------------------ writes

        public async Task ApplyAsync(DateOnly day, WorldAnalyticsDeltas deltas, CancellationToken ct = default)
        {
            if (deltas.IsEmpty) return;
            if (!IsRelational)
            {
                ApplyTracked(day, deltas);
                await _context.SaveChangesAsync(ct);
                return;
            }

            // Sorted by key so concurrent batches take the row locks in the same order.
            var cells = deltas.Cells.OrderBy(c => c.Key.World, StringComparer.Ordinal).ThenBy(c => c.Key.CellSize)
                .ThenBy(c => c.Key.CellX).ThenBy(c => c.Key.CellZ).ToList();
            foreach (var chunk in cells.Chunk(UpsertChunk))
            {
                var sql = new StringBuilder("INSERT INTO `world_movement_cells_daily` (`Day`, `World`, `CellSize`, `CellX`, `CellZ`, `Samples`) VALUES ");
                var parameters = new List<object>(chunk.Length * 6);
                for (var i = 0; i < chunk.Length; i++)
                {
                    var (key, samples) = chunk[i];
                    if (i > 0) sql.Append(", ");
                    sql.Append('(').Append(Param(parameters, day)).Append(", ").Append(Param(parameters, key.World))
                        .Append(", ").Append(Param(parameters, key.CellSize)).Append(", ").Append(Param(parameters, key.CellX))
                        .Append(", ").Append(Param(parameters, key.CellZ)).Append(", ").Append(Param(parameters, samples)).Append(')');
                }
                sql.Append(" ON DUPLICATE KEY UPDATE `Samples` = `Samples` + VALUES(`Samples`)");
                await _context.Database.ExecuteSqlRawAsync(sql.ToString(), parameters, ct);
            }

            var steps = deltas.MenuSteps.OrderBy(s => s.Key.MenuKey, StringComparer.Ordinal)
                .ThenBy(s => s.Key.Step, StringComparer.Ordinal).ThenBy(s => s.Key.Outcome).ToList();
            foreach (var chunk in steps.Chunk(UpsertChunk))
            {
                var sql = new StringBuilder("INSERT INTO `menu_funnel_daily` (`Day`, `MenuKey`, `Step`, `Outcome`, `Count`) VALUES ");
                var parameters = new List<object>(chunk.Length * 5);
                for (var i = 0; i < chunk.Length; i++)
                {
                    var (key, count) = chunk[i];
                    if (i > 0) sql.Append(", ");
                    sql.Append('(').Append(Param(parameters, day)).Append(", ").Append(Param(parameters, key.MenuKey))
                        .Append(", ").Append(Param(parameters, key.Step)).Append(", ").Append(Param(parameters, (byte)key.Outcome))
                        .Append(", ").Append(Param(parameters, count)).Append(')');
                }
                sql.Append(" ON DUPLICATE KEY UPDATE `Count` = `Count` + VALUES(`Count`)");
                await _context.Database.ExecuteSqlRawAsync(sql.ToString(), parameters, ct);
            }

            var domains = deltas.Domains.OrderBy(d => d.Key.DomainId).ThenBy(d => d.Key.Kind, StringComparer.Ordinal).ToList();
            foreach (var chunk in domains.Chunk(UpsertChunk))
            {
                var sql = new StringBuilder("INSERT INTO `domain_interactions_daily` (`Day`, `DomainId`, `Kind`, `Count`, `UniquePlayers`) VALUES ");
                var parameters = new List<object>(chunk.Length * 5);
                for (var i = 0; i < chunk.Length; i++)
                {
                    var (key, value) = chunk[i];
                    if (i > 0) sql.Append(", ");
                    sql.Append('(').Append(Param(parameters, day)).Append(", ").Append(Param(parameters, key.DomainId))
                        .Append(", ").Append(Param(parameters, key.Kind)).Append(", ").Append(Param(parameters, value.Count))
                        .Append(", ").Append(Param(parameters, value.UniquePlayers)).Append(')');
                }
                sql.Append(" ON DUPLICATE KEY UPDATE `Count` = `Count` + VALUES(`Count`), `UniquePlayers` = GREATEST(`UniquePlayers`, VALUES(`UniquePlayers`))");
                await _context.Database.ExecuteSqlRawAsync(sql.ToString(), parameters, ct);
            }
        }

        private void ApplyTracked(DateOnly day, WorldAnalyticsDeltas deltas)
        {
            foreach (var (key, samples) in deltas.Cells)
            {
                var row = _context.WorldMovementCellDailies.Find(day, key.World, key.CellSize, key.CellX, key.CellZ);
                if (row == null)
                {
                    _context.WorldMovementCellDailies.Add(new WorldMovementCellDaily
                    {
                        Day = day, World = key.World, CellSize = key.CellSize, CellX = key.CellX, CellZ = key.CellZ, Samples = samples
                    });
                    continue;
                }
                row.Samples += samples;
            }

            foreach (var (key, count) in deltas.MenuSteps)
            {
                var row = _context.MenuFunnelDailies.Find(day, key.MenuKey, key.Step, key.Outcome);
                if (row == null)
                {
                    _context.MenuFunnelDailies.Add(new MenuFunnelDaily
                    {
                        Day = day, MenuKey = key.MenuKey, Step = key.Step, Outcome = key.Outcome, Count = count
                    });
                    continue;
                }
                row.Count += count;
            }

            foreach (var (key, value) in deltas.Domains)
            {
                var row = _context.DomainInteractionDailies.Find(day, key.DomainId, key.Kind);
                if (row == null)
                {
                    _context.DomainInteractionDailies.Add(new DomainInteractionDaily
                    {
                        Day = day, DomainId = key.DomainId, Kind = key.Kind, Count = value.Count, UniquePlayers = value.UniquePlayers
                    });
                    continue;
                }
                row.Count += value.Count;
                row.UniquePlayers = Math.Max(row.UniquePlayers, value.UniquePlayers);
            }
        }

        private static string Param(List<object> parameters, object value)
        {
            var name = "@p" + parameters.Count;
            parameters.Add(new MySqlParameter(name, value));
            return name;
        }

        // ------------------------------------------------------------------ reads

        public async Task<List<MovementWorldSum>> GetMovementWorldsAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            var rows = await _context.WorldMovementCellDailies.AsNoTracking()
                .Where(c => c.Day >= from && c.Day <= to)
                .GroupBy(c => new { c.World, c.CellSize })
                .Select(g => new { g.Key.World, g.Key.CellSize, Samples = g.Sum(c => (long)c.Samples) })
                .ToListAsync(ct);
            return rows.Select(r => new MovementWorldSum(r.World, r.CellSize, r.Samples)).ToList();
        }

        public async Task<List<MovementCellSum>> GetMovementCellsAsync(string world, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            var rows = await _context.WorldMovementCellDailies.AsNoTracking()
                .Where(c => c.World == world && c.Day >= from && c.Day <= to)
                .GroupBy(c => new { c.CellSize, c.CellX, c.CellZ })
                .Select(g => new { g.Key.CellSize, g.Key.CellX, g.Key.CellZ, Samples = g.Sum(c => (long)c.Samples) })
                .ToListAsync(ct);
            return rows.Select(r => new MovementCellSum(r.CellSize, r.CellX, r.CellZ, r.Samples)).ToList();
        }

        public async Task<List<MenuStepSum>> GetMenuStepsAsync(string? menuKey, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            var query = _context.MenuFunnelDailies.AsNoTracking().Where(s => s.Day >= from && s.Day <= to);
            if (menuKey != null) query = query.Where(s => s.MenuKey == menuKey);
            var rows = await query
                .GroupBy(s => new { s.MenuKey, s.Step, s.Outcome })
                .Select(g => new { g.Key.MenuKey, g.Key.Step, g.Key.Outcome, Count = g.Sum(s => (long)s.Count) })
                .ToListAsync(ct);
            return rows.Select(r => new MenuStepSum(r.MenuKey, r.Step, r.Outcome, r.Count)).ToList();
        }

        public Task<List<DomainInteractionDaily>> GetDomainRowsAsync(string? kind, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            var query = _context.DomainInteractionDailies.AsNoTracking().Where(d => d.Day >= from && d.Day <= to);
            if (kind != null) query = query.Where(d => d.Kind == kind);
            return query.ToListAsync(ct);
        }

        public async Task<Dictionary<int, DomainLabel>> GetDomainLabelsAsync(IReadOnlyCollection<int> domainIds, CancellationToken ct = default)
        {
            if (domainIds.Count == 0) return new Dictionary<int, DomainLabel>();
            var ids = domainIds.Distinct().ToList();
            var rows = await _context.Domains.AsNoTracking()
                .Where(d => ids.Contains(d.Id))
                .Select(d => new { d.Id, d.Name, d.WgRegionId })
                .ToListAsync(ct);
            return rows.ToDictionary(r => r.Id, r => new DomainLabel(r.Id, r.Name, r.WgRegionId));
        }

        // ------------------------------------------------------------------ retention

        public async Task<int> PurgeDailyBeforeAsync(DateOnly day, CancellationToken ct = default)
        {
            var cells = await DeleteAsync(_context.WorldMovementCellDailies.Where(c => c.Day < day), ct);
            var steps = await DeleteAsync(_context.MenuFunnelDailies.Where(s => s.Day < day), ct);
            var domains = await DeleteAsync(_context.DomainInteractionDailies.Where(d => d.Day < day), ct);
            return cells + steps + domains;
        }

        public Task<int> PurgeBatchesBeforeAsync(DateTime before, CancellationToken ct = default) =>
            DeleteAsync(_context.WorldAnalyticsBatches.Where(b => b.ReceivedAt < before), ct);

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
