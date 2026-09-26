using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace knkwebapi_v2.Repositories
{
    /// <summary>
    /// Currency ledger data access (docs/specs/currency-payments/DESIGN.md §3.2). Add and read
    /// only — there is deliberately no update or delete here.
    /// </summary>
    public class CurrencyRepository : ICurrencyRepository
    {
        private readonly KnKDbContext _context;

        public CurrencyRepository(KnKDbContext context)
        {
            _context = context;
        }

        private IQueryable<CurrencyTransaction> WithEntries() =>
            _context.CurrencyTransactions.AsNoTracking().Include(t => t.Entries);

        public Task<CurrencyTransaction?> FindByIdempotencyAsync(string scope, string key, CancellationToken ct = default) =>
            WithEntries().FirstOrDefaultAsync(t => t.IdempotencyScope == scope && t.IdempotencyKey == key, ct);

        public Task<CurrencyTransaction?> FindByIdAsync(long id, CancellationToken ct = default) =>
            WithEntries().FirstOrDefaultAsync(t => t.Id == id, ct);

        public Task<CurrencyTransaction?> FindByPublicIdAsync(string publicId, CancellationToken ct = default) =>
            WithEntries().FirstOrDefaultAsync(t => t.PublicId == publicId, ct);

        public Task<CurrencyTransaction?> FindReversalOfAsync(long transactionId, CancellationToken ct = default) =>
            WithEntries().FirstOrDefaultAsync(t => t.ReversesTransactionId == transactionId, ct);

        public async Task<Dictionary<int, User>> GetUsersForUpdateAsync(IEnumerable<int> userIds, CancellationToken ct = default)
        {
            var ids = userIds.Distinct().ToList();
            var users = await _context.Users.Where(u => ids.Contains(u.Id)).ToListAsync(ct);
            return users.ToDictionary(u => u.Id);
        }

        public async Task<Dictionary<int, BalancesDto>> GetBalancesAsync(IEnumerable<int> userIds, CancellationToken ct = default)
        {
            var ids = userIds.Distinct().ToList();
            var rows = await _context.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new BalancesDto { UserId = u.Id, Coins = u.Coins, Gems = u.Gems, ExperiencePoints = u.ExperiencePoints })
                .ToListAsync(ct);
            return rows.ToDictionary(b => b.UserId);
        }

        public async Task AddTransactionAsync(CurrencyTransaction transaction, CancellationToken ct = default)
        {
            await _context.CurrencyTransactions.AddAsync(transaction, ct);
            await _context.SaveChangesAsync(ct);
        }

        public void Discard(CurrencyTransaction transaction)
        {
            // Copy first: detaching an entry fixes up (shrinks) the navigation collection.
            foreach (var entry in transaction.Entries.ToList())
            {
                _context.Entry(entry).State = EntityState.Detached;
            }
            _context.Entry(transaction).State = EntityState.Detached;
        }

        public bool IsUniqueViolation(DbUpdateException exception) =>
            exception.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry };

        public async Task<long> SumSentSinceAsync(int userId, Currency currency, DateTime since, CancellationToken ct = default)
        {
            return await _context.CurrencyEntries.AsNoTracking()
                .Where(e => e.UserId == userId
                            && e.Currency == currency
                            && e.Amount < 0
                            && e.Transaction.Kind == CurrencyTransactionKind.Transfer
                            && e.Transaction.FromUserId == userId
                            && e.Transaction.CreatedAt >= since)
                .SumAsync(e => -e.Amount, ct);
        }

        public Task<DateTime?> LastTransferAtAsync(int userId, CancellationToken ct = default) =>
            _context.CurrencyTransactions.AsNoTracking()
                .Where(t => t.FromUserId == userId && t.Kind == CurrencyTransactionKind.Transfer)
                .MaxAsync(t => (DateTime?)t.CreatedAt, ct);

        public async Task<PagedResult<LedgerLineDto>> SearchLinesAsync(LedgerQuery query, CancellationToken ct = default)
        {
            var lines = _context.CurrencyEntries.AsNoTracking()
                .Where(e => e.AccountKind == CurrencyAccountKind.User);

            if (query.UserId.HasValue)
            {
                lines = lines.Where(e => e.UserId == query.UserId.Value);
            }
            if (query.Currency.HasValue)
            {
                lines = lines.Where(e => e.Currency == query.Currency.Value);
            }
            if (!string.IsNullOrWhiteSpace(query.ReasonCode))
            {
                lines = lines.Where(e => e.Transaction.ReasonCode == query.ReasonCode);
            }
            if (query.Kind.HasValue)
            {
                lines = lines.Where(e => e.Transaction.Kind == query.Kind.Value);
            }
            if (query.Initiator.HasValue)
            {
                lines = lines.Where(e => e.Transaction.Initiator == query.Initiator.Value);
            }
            if (query.InitiatorUserId.HasValue)
            {
                lines = lines.Where(e => e.Transaction.InitiatorUserId == query.InitiatorUserId.Value);
            }
            if (!string.IsNullOrWhiteSpace(query.InitiatorSearch))
            {
                var term = query.InitiatorSearch.Trim().ToLower();
                lines = lines.Where(e =>
                    (e.Transaction.InitiatorComponent != null && e.Transaction.InitiatorComponent.ToLower().Contains(term))
                    || _context.Users.Any(u => u.Id == e.Transaction.InitiatorUserId && u.Username.ToLower().Contains(term)));
            }
            if (!string.IsNullOrWhiteSpace(query.UserSearch))
            {
                var term = query.UserSearch.Trim().ToLower();
                lines = lines.Where(e => _context.Users.Any(u => u.Id == e.UserId && u.Username.ToLower().Contains(term)));
            }
            if (!string.IsNullOrWhiteSpace(query.SourceType))
            {
                lines = lines.Where(e => e.Transaction.SourceType == query.SourceType);
            }
            if (!string.IsNullOrWhiteSpace(query.SourceRef))
            {
                lines = lines.Where(e => e.Transaction.SourceRef == query.SourceRef);
            }
            if (!string.IsNullOrWhiteSpace(query.CorrelationId))
            {
                lines = lines.Where(e => e.Transaction.CorrelationId == query.CorrelationId);
            }
            if (!string.IsNullOrWhiteSpace(query.TransactionPublicId))
            {
                lines = lines.Where(e => e.Transaction.PublicId == query.TransactionPublicId);
            }
            if (query.From.HasValue)
            {
                lines = lines.Where(e => e.Transaction.CreatedAt >= query.From.Value);
            }
            if (query.To.HasValue)
            {
                lines = lines.Where(e => e.Transaction.CreatedAt < query.To.Value);
            }

            var totalCount = await lines.CountAsync(ct);

            var desc = query.Descending;
            IOrderedQueryable<CurrencyEntry> ordered = query.Sort switch
            {
                LedgerSort.Amount => desc ? lines.OrderByDescending(e => e.Amount) : lines.OrderBy(e => e.Amount),
                LedgerSort.UserId => desc ? lines.OrderByDescending(e => e.UserId) : lines.OrderBy(e => e.UserId),
                LedgerSort.Currency => desc ? lines.OrderByDescending(e => e.Currency) : lines.OrderBy(e => e.Currency),
                LedgerSort.ReasonCode => desc ? lines.OrderByDescending(e => e.Transaction.ReasonCode) : lines.OrderBy(e => e.Transaction.ReasonCode),
                LedgerSort.Initiator => desc
                    ? lines.OrderByDescending(e => e.Transaction.Initiator).ThenByDescending(e => e.Transaction.InitiatorComponent)
                    : lines.OrderBy(e => e.Transaction.Initiator).ThenBy(e => e.Transaction.InitiatorComponent),
                // Entry ids grow with posting order, so this is creation order without a join sort.
                _ => desc ? lines.OrderByDescending(e => e.Id) : lines.OrderBy(e => e.Id)
            };
            // Stable paging for ties.
            ordered = desc ? ordered.ThenByDescending(e => e.Id) : ordered.ThenBy(e => e.Id);

            var items = await ordered
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(e => new LedgerLineDto
                {
                    EntryId = e.Id,
                    TransactionId = e.TransactionId,
                    PublicId = e.Transaction.PublicId,
                    CreatedAt = e.Transaction.CreatedAt,
                    UserId = e.UserId!.Value,
                    Username = _context.Users.Where(u => u.Id == e.UserId).Select(u => u.Username).FirstOrDefault(),
                    Currency = e.Currency.ToString(),
                    Operation = e.Operation.ToString(),
                    Amount = e.Amount,
                    BalanceBefore = e.BalanceBefore!.Value,
                    BalanceAfter = e.BalanceAfter!.Value,
                    Kind = e.Transaction.Kind.ToString(),
                    ReasonCode = e.Transaction.ReasonCode,
                    Reason = e.Transaction.Reason,
                    Initiator = e.Transaction.Initiator.ToString(),
                    InitiatorUserId = e.Transaction.InitiatorUserId,
                    InitiatorUsername = _context.Users.Where(u => u.Id == e.Transaction.InitiatorUserId).Select(u => u.Username).FirstOrDefault(),
                    InitiatorComponent = e.Transaction.InitiatorComponent,
                    SourceType = e.Transaction.SourceType,
                    SourceRef = e.Transaction.SourceRef,
                    CorrelationId = e.Transaction.CorrelationId,
                    ReversesTransactionId = e.Transaction.ReversesTransactionId,
                    MetadataJson = e.Transaction.MetadataJson
                })
                .ToListAsync(ct);

            return new PagedResult<LedgerLineDto>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.Page,
                PageSize = query.PageSize
            };
        }
    }
}
