using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Stock
{
    /// <summary>
    /// The only way stock quantities change (production DESIGN § 4.3-4.5, PandiO/knk-workspace#97). Every request is
    /// one double-entry <see cref="StockTransaction"/>: each line debits one storage and credits another.
    /// </summary>
    public interface IStockService
    {
        /// <summary>One line; see <see cref="MoveManyAsync"/>.</summary>
        Task<StockResult> MoveAsync(StockLineRequest line, StockContext ctx, StockMoveMode mode = StockMoveMode.AllOrNothing,
            CancellationToken ct = default);

        /// <summary>
        /// Moves every line in one transaction. <see cref="StockMoveMode.AllOrNothing"/>: every line in full or nothing;
        /// <see cref="StockMoveMode.Partial"/>: each line moves what fits and the rest stays in the source. Capacity,
        /// missing stock and locks come back in the result, never as an exception. Throws
        /// <see cref="ArgumentException"/> for a malformed request, <see cref="KeyNotFoundException"/> for a missing
        /// storage and <see cref="StockIdempotencyConflictException"/> when the key was used for another request.
        /// Runs inside the caller's database transaction when there is one (it is then the caller's to commit).
        /// </summary>
        Task<StockResult> MoveManyAsync(IReadOnlyList<StockLineRequest> lines, StockContext ctx,
            StockMoveMode mode = StockMoveMode.AllOrNothing, CancellationToken ct = default);

        /// <summary>A staff correction: <paramref name="delta"/> of a blueprint (or ±1 unique item) between the storage
        /// and <c>SYS_ADJUSTMENT</c>, reason <see cref="StockReason.AdminAdjust"/>.</summary>
        Task<StockResult> AdjustAsync(int storageId, int? itemBlueprintId, long? itemInstanceId, long delta, StockContext ctx,
            CancellationToken ct = default);

        /// <summary>A compensating transaction that moves every move of <paramref name="transactionId"/> back (all or
        /// nothing, reason <see cref="StockReason.Correction"/>). A transaction is reversed at most once (409).</summary>
        Task<StockResult> ReverseAsync(long transactionId, StockContext ctx, CancellationToken ct = default);

        /// <summary>The id of a virtual storage (<c>SYS_PRODUCTION</c>, …); created if missing.</summary>
        Task<int> GetSystemStorageIdAsync(string systemKey, CancellationToken ct = default);
    }

    /// <summary>
    /// <para>
    /// Concurrency (DESIGN § 4.5, V2 defect 4): one database transaction (or the caller's); every storage of the request
    /// is locked <c>FOR UPDATE</c> in ascending id order (no deadlocks between requests), then — under the lock — the
    /// idempotency lookup, the stock, capacity and lock checks, and one SaveChanges that writes the ledger rows, the
    /// stock lines and units and the storage totals together. Each touched storage's <c>RowVersion</c> goes up, a
    /// conditional update (<c>WHERE RowVersion = …</c>) on top of the lock. Two identical requests serialize on the lock
    /// and the second one replays; the unique idempotency index catches the rest (same key, other storages).
    /// </para>
    /// <para>
    /// Virtual storages are unbounded and may go negative, so for every blueprint the sum over all storages is zero.
    /// Real storages never go below zero; capacity limits real storages only (Domain, Player, Clan), in raw units ×
    /// <see cref="ItemBlueprint.StorageWeight"/> (§ 4.4). Lines that reach zero are deleted.
    /// </para>
    /// </summary>
    public sealed class StockService : IStockService
    {
        public const int MaxLinesPerRequest = 500;
        public const long MaxAmount = 1_000_000_000_000L;
        private static readonly Regex KeyPattern = new("^[A-Za-z0-9:_.\\-]{1,100}$", RegexOptions.Compiled);

        private readonly KnKDbContext _db;
        private readonly TimeProvider _clock;

        public StockService(KnKDbContext db, TimeProvider? clock = null)
        {
            _db = db;
            _clock = clock ?? TimeProvider.System;
        }

        public Task<StockResult> MoveAsync(StockLineRequest line, StockContext ctx, StockMoveMode mode = StockMoveMode.AllOrNothing,
            CancellationToken ct = default) =>
            MoveManyAsync(new[] { line ?? throw new ArgumentNullException(nameof(line)) }, ctx, mode, ct);

        public Task<StockResult> MoveManyAsync(IReadOnlyList<StockLineRequest> lines, StockContext ctx,
            StockMoveMode mode = StockMoveMode.AllOrNothing, CancellationToken ct = default)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (ctx.Reason == StockReason.Correction)
                throw new ArgumentException("A correction is written by reversing a transaction (ReverseAsync).");
            return ExecuteAsync(lines, ctx, mode, reverses: null, ct);
        }

        public async Task<StockResult> AdjustAsync(int storageId, int? itemBlueprintId, long? itemInstanceId, long delta,
            StockContext ctx, CancellationToken ct = default)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (delta == 0) throw new ArgumentException("An adjustment needs a non-zero amount.");
            if (itemInstanceId.HasValue && Math.Abs(delta) != 1)
                throw new ArgumentException("A unique item is adjusted by +1 (into the storage) or -1 (out of it).");
            if (string.IsNullOrWhiteSpace(ctx.Note))
                throw new ArgumentException("An adjustment needs a note that says why.");
            int sys = await GetSystemStorageIdAsync(Storage.SysAdjustment, ct);
            long amount = delta == long.MinValue ? long.MaxValue : Math.Abs(delta);
            var line = delta > 0
                ? new StockLineRequest(sys, storageId, itemInstanceId.HasValue ? null : itemBlueprintId, amount, itemInstanceId)
                : new StockLineRequest(storageId, sys, itemInstanceId.HasValue ? null : itemBlueprintId, amount, itemInstanceId);
            var adjust = new StockContext
            {
                Reason = StockReason.AdminAdjust,
                ActorKind = ctx.ActorKind,
                ActorUserId = ctx.ActorUserId,
                ActorComponent = ctx.ActorComponent,
                SourceType = ctx.SourceType,
                SourceRef = ctx.SourceRef,
                Note = ctx.Note,
                IdempotencyKey = ctx.IdempotencyKey,
            };
            return await ExecuteAsync(new[] { line }, adjust, StockMoveMode.AllOrNothing, reverses: null, ct);
        }

        public async Task<StockResult> ReverseAsync(long transactionId, StockContext ctx, CancellationToken ct = default)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            var original = await _db.StockTransactions.AsNoTracking().Include(t => t.Moves)
                .FirstOrDefaultAsync(t => t.Id == transactionId, ct)
                ?? throw new KeyNotFoundException($"Stock transaction {transactionId} not found.");
            if (original.Moves.Count == 0)
                throw new InvalidOperationException($"Stock transaction {transactionId} has no moves to reverse.");
            var lines = original.Moves.OrderBy(m => m.LineNo).ThenBy(m => m.Id)
                .Select(m => new StockLineRequest(m.ToStorageId, m.FromStorageId, m.ItemBlueprintId, m.Amount, m.ItemInstanceId))
                .ToList();
            var correction = new StockContext
            {
                Reason = StockReason.Correction,
                ActorKind = ctx.ActorKind,
                ActorUserId = ctx.ActorUserId,
                ActorComponent = ctx.ActorComponent,
                SourceType = ctx.SourceType ?? nameof(StockTransaction),
                SourceRef = ctx.SourceRef ?? transactionId.ToString(),
                Note = ctx.Note,
                IdempotencyKey = ctx.IdempotencyKey ?? $"reverse:{transactionId}",
            };
            return await ExecuteAsync(lines, correction, StockMoveMode.AllOrNothing, reverses: transactionId, ct);
        }

        public async Task<int> GetSystemStorageIdAsync(string systemKey, CancellationToken ct = default)
        {
            if (!Storage.SystemKeys.Contains(systemKey))
                throw new ArgumentException($"'{systemKey}' is not a system storage.", nameof(systemKey));
            var id = await _db.Storages.Where(s => s.SystemKey == systemKey).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
            if (id.HasValue) return id.Value;
            // The migration seeds all five; this covers a database created without migrations (tests).
            var storage = new Storage { Name = systemKey, Kind = StorageKind.Virtual, SystemKey = systemKey, CreatedAt = Now() };
            _db.Storages.Add(storage);
            await _db.SaveChangesAsync(ct);
            return storage.Id;
        }

        // ===== Core =====

        private async Task<StockResult> ExecuteAsync(IReadOnlyList<StockLineRequest> lines, StockContext ctx, StockMoveMode mode,
            long? reverses, CancellationToken ct)
        {
            Validate(lines, ctx);
            var hash = RequestHash(lines, ctx, mode, reverses);
            var storageIds = lines.SelectMany(l => new[] { l.FromStorageId, l.ToStorageId }).Distinct().OrderBy(i => i).ToList();

            bool relational = _db.Database.IsRelational();
            bool owns = relational && _db.Database.CurrentTransaction == null;
            await using var tx = owns ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : null;
            try
            {
                await StockLocks.LockStoragesAsync(_db, storageIds, ct);
                DetachUnchanged(storageIds);

                if (ctx.IdempotencyKey != null)
                {
                    var replay = await ReplayAsync(ctx.IdempotencyKey, hash, lines, ct);
                    if (replay != null)
                    {
                        if (tx != null) await tx.CommitAsync(ct);
                        return replay;
                    }
                }
                if (reverses.HasValue && await _db.StockTransactions.AnyAsync(t => t.ReversesTransactionId == reverses, ct))
                    throw new InvalidOperationException($"Stock transaction {reverses} was already reversed.");

                var result = await PlanAndApplyAsync(lines, ctx, mode, reverses, hash, ct);
                if (tx != null) await tx.CommitAsync(ct);
                return result;
            }
            catch (DbUpdateException) when (owns && ctx.IdempotencyKey != null)
            {
                await tx!.RollbackAsync(CancellationToken.None);
                DetachStockEntries();
                // A concurrent request with the same key (on other storages) won the unique index: replay its result.
                var replay = await ReplayAsync(ctx.IdempotencyKey, hash, lines, ct);
                if (replay != null) return replay;
                throw;
            }
            catch
            {
                if (tx != null) await tx.RollbackAsync(CancellationToken.None);
                DetachStockEntries();
                throw;
            }
        }

        private async Task<StockResult> PlanAndApplyAsync(IReadOnlyList<StockLineRequest> lines, StockContext ctx,
            StockMoveMode mode, long? reverses, string hash, CancellationToken ct)
        {
            var now = Now();
            var storageIds = lines.SelectMany(l => new[] { l.FromStorageId, l.ToStorageId }).Distinct().ToList();
            var storages = await _db.Storages.Where(s => storageIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
            foreach (var id in storageIds)
            {
                if (!storages.ContainsKey(id)) throw new KeyNotFoundException($"Storage {id} not found.");
            }
            if (ctx.Reason == StockReason.AdminAdjust
                && !lines.All(l => storages[l.FromStorageId].SystemKey == Storage.SysAdjustment
                                   || storages[l.ToStorageId].SystemKey == Storage.SysAdjustment))
                throw new ArgumentException($"An {StockReason.AdminAdjust} move has {Storage.SysAdjustment} on one side.");

            var blueprintIds = lines.Where(l => l.ItemBlueprintId.HasValue).Select(l => l.ItemBlueprintId!.Value).Distinct().ToList();
            var instanceIds = lines.Where(l => l.ItemInstanceId.HasValue).Select(l => l.ItemInstanceId!.Value).Distinct().ToList();

            var weights = await _db.ItemBlueprints.Where(b => blueprintIds.Contains(b.Id))
                .Select(b => new { b.Id, b.StorageWeight }).ToDictionaryAsync(b => b.Id, b => b.StorageWeight, ct);
            foreach (var id in blueprintIds)
            {
                if (!weights.ContainsKey(id)) throw new ArgumentException($"Item blueprint {id} not found.");
            }
            var instances = await _db.ItemInstances.Where(i => instanceIds.Contains(i.Id))
                .Select(i => new { i.Id, i.ItemBlueprintId, i.ItemBlueprint.StorageWeight })
                .ToDictionaryAsync(i => i.Id, ct);
            foreach (var id in instanceIds)
            {
                if (!instances.ContainsKey(id)) throw new ArgumentException($"Item instance {id} not found.");
            }

            var existingLines = await _db.StockLines
                .Where(l => storageIds.Contains(l.StorageId) && blueprintIds.Contains(l.ItemBlueprintId))
                .ToDictionaryAsync(l => (l.StorageId, l.ItemBlueprintId), ct);
            var existingUnits = await _db.StockUnits.Where(u => instanceIds.Contains(u.ItemInstanceId))
                .ToDictionaryAsync(u => u.ItemInstanceId, ct);

            // Capacity use (raw units × weight) of the capped destinations, read under the lock.
            var cappedIds = lines.Select(l => l.ToStorageId).Distinct()
                .Where(id => storages[id].IsCapped && storages[id].MaxUnits.HasValue).ToList();
            var use = cappedIds.ToDictionary(id => id, _ => 0m);
            if (cappedIds.Count > 0)
            {
                var lineUse = await _db.StockLines.Where(l => cappedIds.Contains(l.StorageId))
                    .GroupBy(l => l.StorageId)
                    .Select(g => new { g.Key, Use = g.Sum(l => l.Amount * l.ItemBlueprint.StorageWeight) })
                    .ToListAsync(ct);
                foreach (var r in lineUse) use[r.Key] += r.Use;
                var unitUse = await _db.StockUnits.Where(u => cappedIds.Contains(u.StorageId))
                    .GroupBy(u => u.StorageId)
                    .Select(g => new { g.Key, Use = g.Sum(u => u.ItemInstance.ItemBlueprint.StorageWeight) })
                    .ToListAsync(ct);
                foreach (var r in unitUse) use[r.Key] += r.Use;
            }

            // Running state: every line sees the lines before it.
            var amounts = existingLines.ToDictionary(kv => kv.Key, kv => kv.Value.Amount);
            var unitAt = instanceIds.ToDictionary(id => id, id => existingUnits.TryGetValue(id, out var u) ? (int?)u.StorageId : null);
            var totals = storages.ToDictionary(kv => kv.Key, kv => kv.Value.TotalUnits);
            var distinct = storages.ToDictionary(kv => kv.Key, kv => kv.Value.DistinctItems);
            long Amount(int sid, int bp) => amounts.TryGetValue((sid, bp), out var a) ? a : 0;

            var results = new List<StockLineResult>(lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                var req = lines[i];
                var from = storages[req.FromStorageId];
                var to = storages[req.ToStorageId];
                long accepted;
                var reason = StockRejectReason.None;
                bool unique = req.ItemInstanceId.HasValue;
                decimal weight = unique ? instances[req.ItemInstanceId!.Value].StorageWeight : weights[req.ItemBlueprintId!.Value];

                if (from.IsLockedAt(now) || to.IsLockedAt(now))
                {
                    accepted = 0;
                    reason = StockRejectReason.Locked;
                }
                else if (unique)
                {
                    var at = unitAt[req.ItemInstanceId!.Value];
                    bool inSource = at == from.Id || (at == null && from.IsVirtual);
                    accepted = inSource ? 1 : 0;
                    if (!inSource) reason = StockRejectReason.NotEnoughStock;
                    if (accepted > 0 && to.IsCapped)
                    {
                        if (to.MaxDistinct.HasValue && distinct[to.Id] >= to.MaxDistinct.Value)
                        {
                            accepted = 0;
                            reason = StockRejectReason.DistinctCapReached;
                        }
                        else if (to.MaxUnits.HasValue && use[to.Id] + weight > to.MaxUnits.Value)
                        {
                            accepted = 0;
                            reason = StockRejectReason.CapacityFull;
                        }
                    }
                }
                else
                {
                    int bp = req.ItemBlueprintId!.Value;
                    long available = from.IsVirtual ? long.MaxValue : Math.Max(0, Amount(from.Id, bp));
                    accepted = Math.Min(req.Amount, available);
                    if (accepted < req.Amount) reason = StockRejectReason.NotEnoughStock;
                    if (accepted > 0 && to.IsCapped)
                    {
                        if (Amount(to.Id, bp) == 0 && to.MaxDistinct.HasValue && distinct[to.Id] >= to.MaxDistinct.Value)
                        {
                            accepted = 0;
                            reason = StockRejectReason.DistinctCapReached;
                        }
                        else if (to.MaxUnits.HasValue)
                        {
                            decimal free = to.MaxUnits.Value - use[to.Id];
                            long fits = free <= 0 ? 0 : (long)Math.Min(decimal.Floor(free / weight), MaxAmount);
                            if (fits < accepted)
                            {
                                accepted = fits;
                                reason = StockRejectReason.CapacityFull;
                            }
                        }
                    }
                }

                if (mode == StockMoveMode.AllOrNothing && accepted < req.Amount) accepted = 0;
                if (accepted > 0)
                {
                    if (unique)
                    {
                        unitAt[req.ItemInstanceId!.Value] = to.IsVirtual ? null : to.Id;
                        if (!from.IsVirtual) { totals[from.Id] -= 1; distinct[from.Id] -= 1; }
                        if (!to.IsVirtual) { totals[to.Id] += 1; distinct[to.Id] += 1; }
                    }
                    else
                    {
                        int bp = req.ItemBlueprintId!.Value;
                        long fromBefore = Amount(from.Id, bp), toBefore = Amount(to.Id, bp);
                        amounts[(from.Id, bp)] = fromBefore - accepted;
                        amounts[(to.Id, bp)] = toBefore + accepted;
                        totals[from.Id] -= accepted;
                        totals[to.Id] += accepted;
                        distinct[from.Id] += NonZeroDelta(fromBefore, fromBefore - accepted);
                        distinct[to.Id] += NonZeroDelta(toBefore, toBefore + accepted);
                    }
                    if (use.ContainsKey(to.Id)) use[to.Id] += accepted * weight;
                    if (use.ContainsKey(from.Id)) use[from.Id] -= accepted * weight;
                }
                results.Add(new StockLineResult(i, req.FromStorageId, req.ToStorageId, req.ItemBlueprintId, req.ItemInstanceId,
                    req.Amount, accepted, req.Amount - accepted, accepted < req.Amount ? reason : StockRejectReason.None));
            }

            bool anyShort = results.Any(r => r.Rejected > 0);
            if (mode == StockMoveMode.AllOrNothing && anyShort)
            {
                return new StockResult
                {
                    Status = StockResultStatus.Rejected,
                    Lines = results.Select(r => r.Rejected > 0 ? r
                        : r with { Accepted = 0, Rejected = r.Requested, RejectReason = StockRejectReason.OtherLineRejected }).ToList(),
                };
            }
            if (results.All(r => r.Accepted == 0))
            {
                return new StockResult { Status = StockResultStatus.Rejected, Lines = results };
            }

            // ---- Write: ledger rows, lines, units and totals in one SaveChanges ----
            var transaction = new StockTransaction
            {
                Reason = ctx.Reason,
                Mode = mode,
                ActorKind = ctx.ActorKind,
                ActorUserId = ctx.ActorUserId,
                ActorComponent = Clean(ctx.ActorComponent),
                SourceType = Clean(ctx.SourceType),
                SourceRef = Clean(ctx.SourceRef),
                Note = Clean(ctx.Note),
                IdempotencyKey = ctx.IdempotencyKey,
                RequestHash = hash,
                ReversesTransactionId = reverses,
                CreatedAt = now,
            };
            foreach (var r in results.Where(r => r.Accepted > 0))
            {
                transaction.Moves.Add(new StockMove
                {
                    LineNo = r.LineNo,
                    FromStorageId = r.FromStorageId,
                    ToStorageId = r.ToStorageId,
                    ItemBlueprintId = r.ItemInstanceId.HasValue ? null : r.ItemBlueprintId,
                    ItemInstanceId = r.ItemInstanceId,
                    Amount = r.Accepted,
                    Reason = ctx.Reason,
                    CreatedAt = now,
                });
            }
            _db.StockTransactions.Add(transaction);

            foreach (var ((sid, bp), amount) in amounts)
            {
                existingLines.TryGetValue((sid, bp), out var line);
                if (line == null)
                {
                    if (amount != 0)
                        _db.StockLines.Add(new StockLine { StorageId = sid, ItemBlueprintId = bp, Amount = amount, UpdatedAt = now });
                }
                else if (amount == 0)
                {
                    _db.StockLines.Remove(line);
                }
                else if (line.Amount != amount)
                {
                    line.Amount = amount;
                    line.UpdatedAt = now;
                }
            }
            foreach (var (instanceId, at) in unitAt)
            {
                existingUnits.TryGetValue(instanceId, out var unit);
                if (unit == null)
                {
                    if (at.HasValue) _db.StockUnits.Add(new StockUnit { StorageId = at.Value, ItemInstanceId = instanceId, AddedAt = now });
                }
                else if (!at.HasValue)
                {
                    _db.StockUnits.Remove(unit);
                }
                else if (unit.StorageId != at.Value)
                {
                    unit.StorageId = at.Value;
                    unit.AddedAt = now;
                }
            }
            var touched = results.Where(r => r.Accepted > 0).SelectMany(r => new[] { r.FromStorageId, r.ToStorageId }).Distinct();
            foreach (var sid in touched)
            {
                var s = storages[sid];
                s.TotalUnits = totals[sid];
                s.DistinctItems = distinct[sid];
                s.RowVersion++;
            }

            await _db.SaveChangesAsync(ct);
            return new StockResult
            {
                Status = anyShort ? StockResultStatus.PartiallyApplied : StockResultStatus.Applied,
                TransactionId = transaction.Id,
                Lines = results,
            };
        }

        private async Task<StockResult?> ReplayAsync(string key, string hash, IReadOnlyList<StockLineRequest> lines, CancellationToken ct)
        {
            var existing = await _db.StockTransactions.AsNoTracking().Include(t => t.Moves)
                .FirstOrDefaultAsync(t => t.IdempotencyKey == key, ct);
            if (existing == null) return null;
            if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
                throw new StockIdempotencyConflictException(key);
            var results = lines.Select((l, i) =>
            {
                long accepted = existing.Moves.Where(m => m.LineNo == i).Sum(m => m.Amount);
                return new StockLineResult(i, l.FromStorageId, l.ToStorageId, l.ItemBlueprintId, l.ItemInstanceId, l.Amount,
                    accepted, l.Amount - accepted, StockRejectReason.None);
            }).ToList();
            return new StockResult
            {
                Status = results.All(r => r.Rejected == 0) ? StockResultStatus.Applied : StockResultStatus.PartiallyApplied,
                TransactionId = existing.Id,
                Replayed = true,
                Lines = results,
            };
        }

        // ===== Validation and helpers =====

        private static void Validate(IReadOnlyList<StockLineRequest> lines, StockContext ctx)
        {
            if (lines == null || lines.Count == 0) throw new ArgumentException("A stock request needs at least one line.");
            if (lines.Count > MaxLinesPerRequest) throw new ArgumentException($"At most {MaxLinesPerRequest} lines per request.");
            if (!Enum.IsDefined(ctx.Reason)) throw new ArgumentException("Unknown stock reason.");
            if (!Enum.IsDefined(ctx.ActorKind)) throw new ArgumentException("Unknown actor kind.");
            if (ctx.IdempotencyKey != null && !KeyPattern.IsMatch(ctx.IdempotencyKey))
                throw new ArgumentException("An idempotency key is 1-100 characters: letters, digits and : _ . -");
            CheckLength(ctx.ActorComponent, StockTransaction.ActorComponentMaxLength, "Actor component");
            CheckLength(ctx.SourceType, StockTransaction.SourceTypeMaxLength, "Source type");
            CheckLength(ctx.SourceRef, StockTransaction.SourceRefMaxLength, "Source reference");
            CheckLength(ctx.Note, StockTransaction.NoteMaxLength, "Note");
            var seenUnits = new HashSet<long>();
            foreach (var l in lines)
            {
                if (l == null) throw new ArgumentException("A stock line is missing.");
                if (l.FromStorageId <= 0 || l.ToStorageId <= 0) throw new ArgumentException("A stock line needs a source and a target storage.");
                if (l.FromStorageId == l.ToStorageId) throw new ArgumentException("A stock line moves between two different storages.");
                if (l.ItemBlueprintId.HasValue == l.ItemInstanceId.HasValue)
                    throw new ArgumentException("A stock line has either an item blueprint (with an amount) or a unique item.");
                if (l.ItemInstanceId.HasValue)
                {
                    if (l.Amount != 1) throw new ArgumentException("A unique item moves with amount 1.");
                    if (!seenUnits.Add(l.ItemInstanceId.Value))
                        throw new ArgumentException($"Unique item {l.ItemInstanceId} appears twice in one request.");
                }
                else if (l.Amount <= 0 || l.Amount > MaxAmount)
                {
                    throw new ArgumentException($"A stock line's amount is between 1 and {MaxAmount}.");
                }
            }
        }

        private static void CheckLength(string? value, int max, string what)
        {
            if (value != null && value.Trim().Length > max) throw new ArgumentException($"{what} is at most {max} characters.");
        }

        private static string RequestHash(IReadOnlyList<StockLineRequest> lines, StockContext ctx, StockMoveMode mode, long? reverses)
        {
            var sb = new StringBuilder();
            sb.Append(ctx.Reason).Append('|').Append(mode).Append('|').Append(reverses?.ToString() ?? "-").Append('|');
            foreach (var l in lines)
            {
                sb.Append(l.FromStorageId).Append('>').Append(l.ToStorageId).Append(':')
                    .Append(l.ItemBlueprintId?.ToString() ?? "-").Append(':').Append(l.ItemInstanceId?.ToString() ?? "-")
                    .Append(':').Append(l.Amount).Append(';');
            }
            sb.Append("|source:").Append(ctx.SourceType).Append(':').Append(ctx.SourceRef);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
        }

        private static int NonZeroDelta(long before, long after) => (before == 0, after == 0) switch
        {
            (true, false) => 1,
            (false, true) => -1,
            _ => 0,
        };

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private DateTime Now() => _clock.GetUtcNow().UtcDateTime;

        /// <summary>Rows read before the lock may be stale: drop unchanged tracked copies so the reads under the lock
        /// load the current values.</summary>
        private void DetachUnchanged(IReadOnlyCollection<int> storageIds)
        {
            foreach (var e in _db.ChangeTracker.Entries().ToList())
            {
                if (e.State != EntityState.Unchanged) continue;
                bool stale = e.Entity switch
                {
                    Storage s => storageIds.Contains(s.Id),
                    StockLine l => storageIds.Contains(l.StorageId),
                    StockUnit => true,
                    _ => false,
                };
                if (stale) e.State = EntityState.Detached;
            }
        }

        /// <summary>After a failed write nothing of it may ride along on a later SaveChanges of the same request.</summary>
        private void DetachStockEntries()
        {
            foreach (var e in _db.ChangeTracker.Entries().ToList())
            {
                if (e.Entity is Storage or StockLine or StockUnit or StockTransaction or StockMove)
                    e.State = EntityState.Detached;
            }
        }
    }

    /// <summary>Row locks on storages, in ascending id order (no deadlocks between two requests).</summary>
    internal static class StockLocks
    {
        public static async Task LockStoragesAsync(KnKDbContext db, IEnumerable<int> storageIds, CancellationToken ct)
        {
            var ids = storageIds.Distinct().OrderBy(i => i).ToList();
            if (ids.Count == 0 || !db.Database.IsRelational()) return;
            // ints only, so the joined list can't inject anything.
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync(
                $"SELECT Id FROM storages WHERE Id IN ({string.Join(",", ids)}) ORDER BY Id FOR UPDATE", ct);
#pragma warning restore EF1002
        }
    }
}
