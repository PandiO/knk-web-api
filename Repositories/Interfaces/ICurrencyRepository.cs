using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories.Interfaces
{
    /// <summary>
    /// Data access for the currency ledger (docs/specs/currency-payments/DESIGN.md §3.2). Append-only
    /// by construction: it can add a transaction and read, and has no update or delete methods
    /// (the MySQL triggers of migration AddCurrencyLedgerImmutabilityTriggers back this up). Row
    /// locking is IUserRepository.RunWithUsersLockedAsync, shared with the pre-ledger balance paths.
    /// Only CurrencyService should use it.
    /// </summary>
    public interface ICurrencyRepository
    {
        /// <summary>The transaction posted under (scope, key), with its entries, untracked; null if none.</summary>
        Task<CurrencyTransaction?> FindByIdempotencyAsync(string scope, string key, CancellationToken ct = default);

        Task<CurrencyTransaction?> FindByIdAsync(long id, CancellationToken ct = default);

        Task<CurrencyTransaction?> FindByPublicIdAsync(string publicId, CancellationToken ct = default);

        /// <summary>The reversal of transaction <paramref name="transactionId"/>, if any.</summary>
        Task<CurrencyTransaction?> FindReversalOfAsync(long transactionId, CancellationToken ct = default);

        /// <summary>The users, tracked, for a balance change. Call inside
        /// RunWithUsersLockedAsync so the values are the locked, current ones.</summary>
        Task<Dictionary<int, User>> GetUsersForUpdateAsync(IEnumerable<int> userIds, CancellationToken ct = default);

        /// <summary>Current balances, untracked (users missing from the result don't exist).</summary>
        Task<Dictionary<int, BalancesDto>> GetBalancesAsync(IEnumerable<int> userIds, CancellationToken ct = default);

        /// <summary>
        /// Adds the transaction and its entries and saves, together with the balance changes made
        /// to the users returned by <see cref="GetUsersForUpdateAsync"/> — one SaveChanges, so the
        /// balance columns and the ledger rows commit together.
        /// </summary>
        Task AddTransactionAsync(CurrencyTransaction transaction, CancellationToken ct = default);

        /// <summary>Stops tracking a transaction whose save failed, so a later SaveChanges in the
        /// same request doesn't try to insert it again. Not a database operation.</summary>
        void Discard(CurrencyTransaction transaction);

        /// <summary>True when the save failed on a unique index (idempotency key, reversal, public id).</summary>
        bool IsUniqueViolation(DbUpdateException exception);

        /// <summary>Total the user sent to other players in <paramref name="currency"/> since
        /// <paramref name="since"/> (transfer daily caps, currency Phase 3).</summary>
        Task<long> SumSentSinceAsync(int userId, Currency currency, DateTime since, CancellationToken ct = default);

        /// <summary>When the user last sent a player transfer (cooldowns, currency Phase 3).</summary>
        Task<DateTime?> LastTransferAtAsync(int userId, CancellationToken ct = default);

        /// <summary>User legs matching the query, newest first by default (balance history, event log).</summary>
        Task<PagedResult<LedgerLineDto>> SearchLinesAsync(LedgerQuery query, CancellationToken ct = default);
    }
}
