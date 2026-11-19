using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.Stock
{
    /// <summary>
    /// Ledger reads for the ledger explorer (stock core P1 #97, web app #675): transactions, moves and roll-up rows,
    /// paged, with the filters <c>storageId</c>, <c>itemBlueprintId</c>, <c>itemInstanceId</c>, <c>reason</c>,
    /// <c>transactionId</c>, <c>actorUserId</c>, <c>sourceType</c>, <c>sourceRef</c>, <c>since</c> and <c>until</c>
    /// (ISO 8601, UTC). The drill-down from a stock line's number to its moves is the move search with
    /// <c>storageId</c> + <c>itemBlueprintId</c>, plus the roll-up search with the same filters for older production.
    /// </summary>
    public interface IStockLedgerService
    {
        Task<PagedResultDto<StockTransactionListDto>> SearchTransactionsAsync(PagedQueryDto query);
        Task<StockTransactionDto?> GetTransactionAsync(long id);
        Task<PagedResultDto<StockMoveDto>> SearchMovesAsync(PagedQueryDto query);
        Task<PagedResultDto<StockMoveRollupDto>> SearchRollupsAsync(PagedQueryDto query);
    }

    public sealed class StockLedgerService : IStockLedgerService
    {
        private readonly KnKDbContext _db;

        public StockLedgerService(KnKDbContext db) => _db = db;

        public async Task<PagedResultDto<StockTransactionListDto>> SearchTransactionsAsync(PagedQueryDto query)
        {
            query ??= new PagedQueryDto();
            var q = _db.StockTransactions.AsNoTracking();
            if (Int(query, "storageId") is { } storageId)
                q = q.Where(t => t.Moves.Any(m => m.FromStorageId == storageId || m.ToStorageId == storageId));
            if (Int(query, "itemBlueprintId") is { } blueprintId) q = q.Where(t => t.Moves.Any(m => m.ItemBlueprintId == blueprintId));
            if (Long(query, "itemInstanceId") is { } instanceId) q = q.Where(t => t.Moves.Any(m => m.ItemInstanceId == instanceId));
            if (Long(query, "transactionId") is { } transactionId) q = q.Where(t => t.Id == transactionId);
            if (Reason(query) is { } reason) q = q.Where(t => t.Reason == reason);
            if (Int(query, "actorUserId") is { } actor) q = q.Where(t => t.ActorUserId == actor);
            if (Text(query, "sourceType") is { } sourceType) q = q.Where(t => t.SourceType == sourceType);
            if (Text(query, "sourceRef") is { } sourceRef) q = q.Where(t => t.SourceRef == sourceRef);
            if (Date(query, "since") is { } since) q = q.Where(t => t.CreatedAt >= since);
            if (Date(query, "until") is { } until) q = q.Where(t => t.CreatedAt < until);
            if (TerritoryPaging.Term(query) is { } term)
                q = q.Where(t => (t.Note != null && t.Note.ToLower().Contains(term))
                    || (t.SourceRef != null && t.SourceRef.ToLower().Contains(term))
                    || (t.IdempotencyKey != null && t.IdempotencyKey.ToLower().Contains(term)));
            q = query.SortDescending || string.IsNullOrEmpty(query.SortBy) ? q.OrderByDescending(t => t.Id) : q.OrderBy(t => t.Id);
            return await TerritoryPaging.PageAsync(q.Select(t => new StockTransactionListDto
            {
                Id = t.Id,
                Reason = t.Reason,
                Mode = t.Mode,
                ActorKind = t.ActorKind,
                ActorUserId = t.ActorUserId,
                ActorComponent = t.ActorComponent,
                SourceType = t.SourceType,
                SourceRef = t.SourceRef,
                Note = t.Note,
                ReversesTransactionId = t.ReversesTransactionId,
                CreatedAt = t.CreatedAt,
                MoveCount = t.Moves.Count,
                TotalAmount = t.Moves.Sum(m => m.Amount),
            }), query);
        }

        public async Task<StockTransactionDto?> GetTransactionAsync(long id)
        {
            var t = await _db.StockTransactions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (t == null) return null;
            var moves = await MoveProjection(_db.StockMoves.AsNoTracking().Where(m => m.TransactionId == id)
                .OrderBy(m => m.LineNo).ThenBy(m => m.Id)).ToListAsync();
            var reversedBy = await _db.StockTransactions.Where(x => x.ReversesTransactionId == id)
                .Select(x => (long?)x.Id).FirstOrDefaultAsync();
            string? username = t.ActorUserId.HasValue
                ? await _db.Users.Where(u => u.Id == t.ActorUserId).Select(u => u.Username).FirstOrDefaultAsync()
                : null;
            return new StockTransactionDto
            {
                Id = t.Id,
                Reason = t.Reason,
                Mode = t.Mode,
                ActorKind = t.ActorKind,
                ActorUserId = t.ActorUserId,
                ActorUsername = username,
                ActorComponent = t.ActorComponent,
                SourceType = t.SourceType,
                SourceRef = t.SourceRef,
                Note = t.Note,
                IdempotencyKey = t.IdempotencyKey,
                ReversesTransactionId = t.ReversesTransactionId,
                ReversedByTransactionId = reversedBy,
                CreatedAt = t.CreatedAt,
                MoveCount = moves.Count,
                TotalAmount = moves.Sum(m => m.Amount),
                Moves = moves,
            };
        }

        public async Task<PagedResultDto<StockMoveDto>> SearchMovesAsync(PagedQueryDto query)
        {
            query ??= new PagedQueryDto();
            var q = _db.StockMoves.AsNoTracking();
            if (Int(query, "storageId") is { } storageId) q = q.Where(m => m.FromStorageId == storageId || m.ToStorageId == storageId);
            if (Int(query, "fromStorageId") is { } fromId) q = q.Where(m => m.FromStorageId == fromId);
            if (Int(query, "toStorageId") is { } toId) q = q.Where(m => m.ToStorageId == toId);
            if (Int(query, "itemBlueprintId") is { } blueprintId) q = q.Where(m => m.ItemBlueprintId == blueprintId);
            if (Long(query, "itemInstanceId") is { } instanceId) q = q.Where(m => m.ItemInstanceId == instanceId);
            if (Long(query, "transactionId") is { } transactionId) q = q.Where(m => m.TransactionId == transactionId);
            if (Reason(query) is { } reason) q = q.Where(m => m.Reason == reason);
            if (Int(query, "actorUserId") is { } actor) q = q.Where(m => m.Transaction.ActorUserId == actor);
            if (Date(query, "since") is { } since) q = q.Where(m => m.CreatedAt >= since);
            if (Date(query, "until") is { } until) q = q.Where(m => m.CreatedAt < until);
            q = query.SortDescending || string.IsNullOrEmpty(query.SortBy) ? q.OrderByDescending(m => m.Id) : q.OrderBy(m => m.Id);
            return await TerritoryPaging.PageAsync(MoveProjection(q), query);
        }

        public async Task<PagedResultDto<StockMoveRollupDto>> SearchRollupsAsync(PagedQueryDto query)
        {
            query ??= new PagedQueryDto();
            var q = _db.StockMoveRollups.AsNoTracking();
            if (Int(query, "storageId") is { } storageId) q = q.Where(r => r.FromStorageId == storageId || r.ToStorageId == storageId);
            if (Int(query, "itemBlueprintId") is { } blueprintId) q = q.Where(r => r.ItemBlueprintId == blueprintId);
            if (Reason(query) is { } reason) q = q.Where(r => r.Reason == reason);
            if (Date(query, "since") is { } since) q = q.Where(r => r.Day >= since.Date);
            if (Date(query, "until") is { } until) q = q.Where(r => r.Day < until);
            q = query.SortDescending || string.IsNullOrEmpty(query.SortBy)
                ? q.OrderByDescending(r => r.Day).ThenByDescending(r => r.Id)
                : q.OrderBy(r => r.Day).ThenBy(r => r.Id);
            return await TerritoryPaging.PageAsync(q.Select(r => new StockMoveRollupDto
            {
                Id = r.Id,
                Day = r.Day,
                Reason = r.Reason,
                FromStorageId = r.FromStorageId,
                FromStorageName = r.FromStorage.Name,
                ToStorageId = r.ToStorageId,
                ToStorageName = r.ToStorage.Name,
                ItemBlueprintId = r.ItemBlueprintId,
                ItemBlueprintName = r.ItemBlueprint.Name,
                Amount = r.Amount,
                MoveCount = r.MoveCount,
                RolledUpAt = r.RolledUpAt,
            }), query);
        }

        private static IQueryable<StockMoveDto> MoveProjection(IQueryable<StockMove> q) => q.Select(m => new StockMoveDto
        {
            Id = m.Id,
            TransactionId = m.TransactionId,
            LineNo = m.LineNo,
            FromStorageId = m.FromStorageId,
            FromStorageName = m.FromStorage.Name,
            ToStorageId = m.ToStorageId,
            ToStorageName = m.ToStorage.Name,
            ItemBlueprintId = m.ItemBlueprintId ?? (m.ItemInstance != null ? m.ItemInstance.ItemBlueprintId : null),
            ItemBlueprintName = m.ItemBlueprint != null ? m.ItemBlueprint.Name
                : m.ItemInstance != null ? m.ItemInstance.ItemBlueprint.Name : null,
            ItemInstanceId = m.ItemInstanceId,
            Amount = m.Amount,
            Reason = m.Reason,
            CreatedAt = m.CreatedAt,
        });

        private static string? Text(PagedQueryDto q, string name) =>
            q.Filters != null && q.Filters.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        private static int? Int(PagedQueryDto q, string name) =>
            Text(q, name) is { } v && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

        private static long? Long(PagedQueryDto q, string name) =>
            Text(q, name) is { } v && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

        private static StockReason? Reason(PagedQueryDto q) =>
            Text(q, "reason") is { } v && Enum.TryParse<StockReason>(v, true, out var r) ? r : null;

        private static DateTime? Date(PagedQueryDto q, string name) =>
            Text(q, name) is { } v && DateTime.TryParse(v, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
                ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null;
    }
}
