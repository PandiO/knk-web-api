using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Stock
{
    public sealed class StockRollupResult
    {
        /// <summary>Moves created before this UTC instant (a day start, 30 days back) were eligible.</summary>
        [JsonPropertyName("cutoff")] public DateTime Cutoff { get; set; }
        [JsonPropertyName("transactionsRolledUp")] public int TransactionsRolledUp { get; set; }
        [JsonPropertyName("movesRolledUp")] public int MovesRolledUp { get; set; }
        [JsonPropertyName("rollupRowsWritten")] public int RollupRowsWritten { get; set; }
    }

    public interface IStockRollupService
    {
        Task<StockRollupResult> RollUpAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// The ledger roll-up (G3, DESIGN § 14 D10, acceptance A22): <c>Production</c> and <c>ProductionInput</c> moves of
    /// whole UTC days more than 30 days back are summed per day, reason, storage pair and blueprint into
    /// <see cref="StockMoveRollup"/> rows; the summed moves and their transactions are deleted in the same database
    /// transaction. Stock lines are not touched, so totals are unchanged and the reconciler stays green. Every other
    /// reason keeps every row. Skipped: transactions with a unique item, and transactions a correction points at.
    /// Safe to rerun and to run twice at once (the second delete of a row fails and that batch rolls back).
    /// </summary>
    public sealed class StockRollupService : IStockRollupService
    {
        public const int RetentionDays = 30;
        public const int BatchSize = 500;

        private static readonly StockReason[] RolledUpReasons = { StockReason.Production, StockReason.ProductionInput };

        private readonly KnKDbContext _db;
        private readonly TimeProvider _clock;

        public StockRollupService(KnKDbContext db, TimeProvider? clock = null)
        {
            _db = db;
            _clock = clock ?? TimeProvider.System;
        }

        /// <summary>The first UTC instant whose moves are kept: the start of the day 30 days back.</summary>
        public static DateTime CutoffFor(DateTime nowUtc) => nowUtc.Date.AddDays(-RetentionDays);

        public async Task<StockRollupResult> RollUpAsync(CancellationToken ct = default)
        {
            var result = new StockRollupResult { Cutoff = CutoffFor(_clock.GetUtcNow().UtcDateTime) };
            var cutoff = result.Cutoff;
            long afterId = 0;
            while (true)
            {
                var batch = await _db.StockTransactions.AsNoTracking()
                    .Where(t => t.Id > afterId && RolledUpReasons.Contains(t.Reason) && t.CreatedAt < cutoff)
                    .Where(t => !t.Moves.Any(m => m.ItemInstanceId != null))
                    .Where(t => !_db.StockTransactions.Any(c => c.ReversesTransactionId == t.Id))
                    .OrderBy(t => t.Id).Select(t => t.Id).Take(BatchSize).ToListAsync(ct);
                if (batch.Count == 0) break;
                afterId = batch[^1];
                var (moves, rows) = await RollUpBatchAsync(batch, ct);
                result.TransactionsRolledUp += batch.Count;
                result.MovesRolledUp += moves;
                result.RollupRowsWritten += rows;
            }
            return result;
        }

        private async Task<(int Moves, int Rows)> RollUpBatchAsync(List<long> transactionIds, CancellationToken ct)
        {
            bool relational = _db.Database.IsRelational();
            await using var tx = relational && _db.Database.CurrentTransaction == null
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
                : null;
            try
            {
                var transactions = await _db.StockTransactions.Include(t => t.Moves)
                    .Where(t => transactionIds.Contains(t.Id)).ToListAsync(ct);
                var moves = transactions.SelectMany(t => t.Moves).ToList();
                var groups = moves.GroupBy(m => (Day: m.CreatedAt.Date, m.Reason, m.FromStorageId, m.ToStorageId,
                        Blueprint: m.ItemBlueprintId!.Value))
                    .ToList();
                var days = groups.Select(g => g.Key.Day).Distinct().ToList();
                var froms = groups.Select(g => g.Key.FromStorageId).Distinct().ToList();
                var existing = await _db.StockMoveRollups
                    .Where(r => days.Contains(r.Day) && froms.Contains(r.FromStorageId))
                    .ToListAsync(ct);
                var byKey = existing.ToDictionary(r => (r.Day, r.Reason, r.FromStorageId, r.ToStorageId, r.ItemBlueprintId));
                var now = _clock.GetUtcNow().UtcDateTime;
                foreach (var g in groups)
                {
                    var key = (g.Key.Day, g.Key.Reason, g.Key.FromStorageId, g.Key.ToStorageId, g.Key.Blueprint);
                    if (!byKey.TryGetValue(key, out var row))
                    {
                        row = new StockMoveRollup
                        {
                            Day = g.Key.Day,
                            Reason = g.Key.Reason,
                            FromStorageId = g.Key.FromStorageId,
                            ToStorageId = g.Key.ToStorageId,
                            ItemBlueprintId = g.Key.Blueprint,
                        };
                        _db.StockMoveRollups.Add(row);
                        byKey[key] = row;
                    }
                    row.Amount += g.Sum(m => m.Amount);
                    row.MoveCount += g.Count();
                    row.RolledUpAt = now;
                }
                _db.StockMoves.RemoveRange(moves);
                _db.StockTransactions.RemoveRange(transactions);
                await _db.SaveChangesAsync(ct);
                if (tx != null) await tx.CommitAsync(ct);
                _db.ChangeTracker.Clear();
                return (moves.Count, groups.Count);
            }
            catch
            {
                if (tx != null) await tx.RollbackAsync(CancellationToken.None);
                _db.ChangeTracker.Clear();
                throw;
            }
        }
    }
}
