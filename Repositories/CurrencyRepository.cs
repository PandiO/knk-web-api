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
            // Ledger rows first: if this insert loses an idempotency race (unique index), no
            // balance has been written yet, so the caller's transaction stays clean.
            await _context.SaveChangesAsync(ct);

            if (!_context.Database.IsRelational())
            {
                // EF InMemory (tests) has no ExecuteUpdate and doesn't apply PropertySaveBehavior,
                // so the SaveChanges above already stored the tracked users' new balances.
                return;
            }

            // Coins/Gems/ExperiencePoints are PropertySaveBehavior.Ignore (KnKDbContext), so
            // SaveChanges never writes them: this is the only write, inside the locked ledger
            // transaction. SaveChanges already made the tracked users' values the "original"
            // ones, so the entities and the rows agree afterwards.
            var finalBalances = transaction.Entries
                .Where(e => e.AccountKind == CurrencyAccountKind.User)
                .GroupBy(e => (UserId: e.UserId!.Value, e.Currency))
                .Select(g => (g.Key.UserId, g.Key.Currency, Value: checked((int)g.Last().BalanceAfter!.Value)));
            foreach (var (userId, currency, value) in finalBalances)
            {
                var row = _context.Users.Where(u => u.Id == userId);
                _ = currency switch
                {
                    Currency.Coins => await row.ExecuteUpdateAsync(set => set.SetProperty(u => u.Coins, value), ct),
                    Currency.Gems => await row.ExecuteUpdateAsync(set => set.SetProperty(u => u.Gems, value), ct),
                    Currency.Experience => await row.ExecuteUpdateAsync(set => set.SetProperty(u => u.ExperiencePoints, value), ct),
                    _ => throw new ArgumentOutOfRangeException(nameof(currency))
                };
            }
        }

        public async Task<Dictionary<Currency, CurrencyPolicy>> GetPoliciesAsync(CancellationToken ct = default) =>
            await _context.CurrencyPolicies.AsNoTracking().ToDictionaryAsync(p => p.Currency, ct);

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

        public async Task<List<(DateTime CreatedAt, long Amount)>> GetTransfersSentSinceAsync(int userId, Currency currency, DateTime since, CancellationToken ct = default)
        {
            // What the recipient got (not the sender's debit, which also carries any fee), per
            // transfer; transfers of the other currency come back as 0 and are dropped.
            var rows = await _context.CurrencyTransactions.AsNoTracking()
                .Where(t => t.FromUserId == userId && t.Kind == CurrencyTransactionKind.Transfer && t.CreatedAt >= since)
                .Select(t => new
                {
                    t.CreatedAt,
                    Amount = t.Entries.Where(e => e.UserId == t.ToUserId && e.Currency == currency && e.Amount > 0).Sum(e => e.Amount)
                })
                .ToListAsync(ct);
            return rows.Where(r => r.Amount > 0).Select(r => (r.CreatedAt, r.Amount)).OrderBy(r => r.CreatedAt).ToList();
        }

        public Task<long> SumReceivedSinceAsync(int userId, Currency currency, DateTime since, CancellationToken ct = default) =>
            _context.CurrencyTransactions.AsNoTracking()
                .Where(t => t.ToUserId == userId && t.Kind == CurrencyTransactionKind.Transfer && t.CreatedAt >= since)
                .SelectMany(t => t.Entries.Where(e => e.UserId == userId && e.Currency == currency && e.Amount > 0))
                .SumAsync(e => e.Amount, ct);

        public Task<DateTime?> LastTransferAtAsync(int userId, CancellationToken ct = default) =>
            _context.CurrencyTransactions.AsNoTracking()
                .Where(t => t.FromUserId == userId && t.Kind == CurrencyTransactionKind.Transfer)
                .MaxAsync(t => (DateTime?)t.CreatedAt, ct);

        public Task<TitleBracket?> GetTitleBracketAsync(int id, CancellationToken ct = default) =>
            _context.TitleBrackets.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);

        // ===== Pending transfers (not ledger rows: their status changes) =====

        public Task<CurrencyPendingTransfer?> FindPendingAsync(string publicId, CancellationToken ct = default) =>
            _context.CurrencyPendingTransfers.FirstOrDefaultAsync(p => p.PublicId == publicId, ct);

        public Task<CurrencyPendingTransfer?> FindPendingByKeyAsync(string idempotencyKey, CancellationToken ct = default) =>
            _context.CurrencyPendingTransfers.FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey, ct);

        public Task<List<CurrencyPendingTransfer>> GetOpenPendingForSenderAsync(int senderUserId, CancellationToken ct = default) =>
            _context.CurrencyPendingTransfers
                .Where(p => p.SenderUserId == senderUserId && p.Status == CurrencyPendingTransferStatus.Pending)
                .ToListAsync(ct);

        public Task ReloadPendingAsync(CurrencyPendingTransfer pending, CancellationToken ct = default) =>
            _context.Entry(pending).ReloadAsync(ct);

        public async Task<Dictionary<int, (string Username, string? Uuid)>> GetIdentitiesAsync(IEnumerable<int> userIds, CancellationToken ct = default)
        {
            var ids = userIds.Distinct().ToList();
            var rows = await _context.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, u.Username, u.Uuid })
                .ToListAsync(ct);
            return rows.ToDictionary(r => r.Id, r => (r.Username, r.Uuid));
        }

        public async Task AddPendingAsync(CurrencyPendingTransfer pending, CancellationToken ct = default)
        {
            await _context.CurrencyPendingTransfers.AddAsync(pending, ct);
            await _context.SaveChangesAsync(ct);
        }

        public Task SavePendingChangesAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);

        public void DiscardPending(CurrencyPendingTransfer pending) =>
            _context.Entry(pending).State = EntityState.Detached;

        // ===== Leaderboard =====

        public async Task<(int TotalCount, List<LeaderboardEntryDto> Entries)> GetLeaderboardAsync(
            Currency currency, string exemptNode, int skip, int take, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            // Exact-node grants only (a user's own, or a group they belong to): wildcard holders
            // such as "*" admins are not left out automatically.
            var exemptHolders = _context.PermissionGrants
                .Where(g => g.Node == exemptNode && g.Value && (g.ExpiresAt == null || g.ExpiresAt > now))
                .Select(g => g.HolderId);
            var exemptMembers = _context.UserPermissionGroups
                .Where(m => exemptHolders.Contains(m.PermissionGroupId) && (m.ExpiresAt == null || m.ExpiresAt > now))
                .Select(m => m.UserId);

            var users = _context.Users.AsNoTracking()
                .Where(u => u.IsActive && u.DeletedAt == null && u.TransferLockReason == null
                            && !exemptHolders.Contains(u.Id) && !exemptMembers.Contains(u.Id));

            var ranked = currency == Currency.Gems
                ? users.Where(u => u.Gems > 0).OrderByDescending(u => u.Gems).ThenBy(u => u.Id)
                    .Select(u => new LeaderboardEntryDto { UserId = u.Id, Username = u.Username, Balance = u.Gems })
                : users.Where(u => u.Coins > 0).OrderByDescending(u => u.Coins).ThenBy(u => u.Id)
                    .Select(u => new LeaderboardEntryDto { UserId = u.Id, Username = u.Username, Balance = u.Coins });

            var total = await ranked.CountAsync(ct);
            var entries = await ranked.Skip(skip).Take(take).ToListAsync(ct);
            for (var i = 0; i < entries.Count; i++)
            {
                entries[i].Rank = skip + i + 1;
            }
            return (total, entries);
        }

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
                LedgerSort.Recipient => desc
                    ? lines.OrderByDescending(e => _context.Users.Where(u => u.Id == e.UserId).Select(u => u.Username).FirstOrDefault())
                    : lines.OrderBy(e => _context.Users.Where(u => u.Id == e.UserId).Select(u => u.Username).FirstOrDefault()),
                LedgerSort.Operation => desc ? lines.OrderByDescending(e => e.Operation) : lines.OrderBy(e => e.Operation),
                LedgerSort.BalanceAfter => desc ? lines.OrderByDescending(e => e.BalanceAfter) : lines.OrderBy(e => e.BalanceAfter),
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
                    MetadataJson = e.Transaction.MetadataJson,
                    CounterpartyUserId = e.Transaction.Kind == CurrencyTransactionKind.Transfer
                        ? (e.UserId == e.Transaction.FromUserId ? e.Transaction.ToUserId : e.Transaction.FromUserId)
                        : null,
                    CounterpartyUsername = e.Transaction.Kind == CurrencyTransactionKind.Transfer
                        ? _context.Users
                            .Where(u => u.Id == (e.UserId == e.Transaction.FromUserId ? e.Transaction.ToUserId : e.Transaction.FromUserId))
                            .Select(u => u.Username).FirstOrDefault()
                        : null
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

        public async Task<long> SumAdminGrantedSinceAsync(int actorUserId, Currency currency, DateTime since, CancellationToken ct = default) =>
            await _context.CurrencyEntries.AsNoTracking()
                .Where(e => e.AccountKind == CurrencyAccountKind.User && e.Currency == currency && e.Amount > 0
                    && e.Transaction.Kind == CurrencyTransactionKind.AdminAdjust
                    && e.Transaction.InitiatorUserId == actorUserId
                    && e.Transaction.CreatedAt >= since)
                .SumAsync(e => (long?)e.Amount, ct) ?? 0;

        public Task<CurrencyPolicy?> GetPolicyForUpdateAsync(Currency currency, CancellationToken ct = default) =>
            _context.CurrencyPolicies.FirstOrDefaultAsync(p => p.Currency == currency, ct);

        public Task SavePolicyAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);
    }
}
