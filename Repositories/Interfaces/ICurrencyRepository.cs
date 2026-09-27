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

        /// <summary>The users whose account was merged into <paramref name="userId"/>'s: the
        /// holders of a MERGE_FORFEIT posting whose source is that user (one level, not transitive).</summary>
        Task<List<int>> GetUsersMergedIntoAsync(int userId, CancellationToken ct = default);

        /// <summary>The users, tracked, for a balance change. Call inside
        /// RunWithUsersLockedAsync so the values are the locked, current ones.</summary>
        Task<Dictionary<int, User>> GetUsersForUpdateAsync(IEnumerable<int> userIds, CancellationToken ct = default);

        /// <summary>Current balances, untracked (users missing from the result don't exist).</summary>
        Task<Dictionary<int, BalancesDto>> GetBalancesAsync(IEnumerable<int> userIds, CancellationToken ct = default);

        /// <summary>
        /// Adds the transaction and its entries, then writes each user leg's final BalanceAfter
        /// to the users row (ExecuteUpdate — the balance columns are PropertySaveBehavior.Ignore,
        /// so this is the only code that can write them). Call inside
        /// IUserRepository.RunWithUsersLockedAsync: the ledger rows and the balance columns commit
        /// together. The users returned by <see cref="GetUsersForUpdateAsync"/> must already hold
        /// the new values.
        /// </summary>
        Task AddTransactionAsync(CurrencyTransaction transaction, CancellationToken ct = default);

        /// <summary>Policy rows per currency, untracked (a currency without a row is missing).</summary>
        Task<Dictionary<Currency, CurrencyPolicy>> GetPoliciesAsync(CancellationToken ct = default);

        /// <summary>Stops tracking a transaction whose save failed, so a later SaveChanges in the
        /// same request doesn't try to insert it again. Not a database operation.</summary>
        void Discard(CurrencyTransaction transaction);

        /// <summary>True when the save failed on a unique index (idempotency key, reversal, public id).</summary>
        bool IsUniqueViolation(DbUpdateException exception);

        /// <summary>Each player transfer of <paramref name="currency"/> the user sent since
        /// <paramref name="since"/>: when, and what the recipient got (fees excluded) less what a
        /// reversal of it took back, oldest first; fully reversed transfers are left out (transfer
        /// daily cap and hourly limit, currency Phase 3, KNG-21). Call under the sender's row lock
        /// so a concurrent transfer can't slip past the cap.</summary>
        Task<List<(DateTime CreatedAt, long Amount)>> GetTransfersSentSinceAsync(int userId, Currency currency, DateTime since, CancellationToken ct = default);

        /// <summary>Total the user received from player transfers of <paramref name="currency"/> since
        /// <paramref name="since"/>, less what reversals of those transfers took back (KNG-21).</summary>
        Task<long> SumReceivedSinceAsync(int userId, Currency currency, DateTime since, CancellationToken ct = default);

        /// <summary>When the user last sent a player transfer of any currency (cooldowns, currency Phase 3).</summary>
        Task<DateTime?> LastTransferAtAsync(int userId, CancellationToken ct = default);

        /// <summary>A title bracket, untracked (the transfer policy's sender title gate).</summary>
        Task<TitleBracket?> GetTitleBracketAsync(int id, CancellationToken ct = default);

        // Pending transfers (currency Phase 3) are workflow rows, not ledger rows: their status
        // moves Pending → Confirmed/Cancelled/Expired, so they are tracked and updated.

        /// <summary>The pending transfer, tracked; null if none.</summary>
        Task<CurrencyPendingTransfer?> FindPendingAsync(string publicId, CancellationToken ct = default);

        /// <summary>The pending transfer created under this (scope-prefixed) key, tracked; null if none.</summary>
        Task<CurrencyPendingTransfer?> FindPendingByKeyAsync(string idempotencyKey, CancellationToken ct = default);

        /// <summary>The sender's transfers still in status Pending (expired or not), tracked.</summary>
        Task<List<CurrencyPendingTransfer>> GetOpenPendingForSenderAsync(int senderUserId, CancellationToken ct = default);

        /// <summary>Re-reads a tracked pending transfer (call under the sender's lock before deciding on it).</summary>
        Task ReloadPendingAsync(CurrencyPendingTransfer pending, CancellationToken ct = default);

        /// <summary>Username and Minecraft UUID per user id (users missing from the result don't exist).</summary>
        Task<Dictionary<int, (string Username, string? Uuid)>> GetIdentitiesAsync(IEnumerable<int> userIds, CancellationToken ct = default);

        Task AddPendingAsync(CurrencyPendingTransfer pending, CancellationToken ct = default);

        /// <summary>Saves status changes made to tracked pending transfers.</summary>
        Task SavePendingChangesAsync(CancellationToken ct = default);

        /// <summary>Stops tracking a pending transfer whose insert failed.</summary>
        void DiscardPending(CurrencyPendingTransfer pending);

        /// <summary>
        /// Active, non-deleted, not transfer-locked users with a positive balance of
        /// <paramref name="currency"/>, richest first (ties by id), leaving out holders of an
        /// exact, unexpired grant of <paramref name="exemptNode"/> (directly or through a group).
        /// </summary>
        Task<(int TotalCount, List<LeaderboardEntryDto> Entries)> GetLeaderboardAsync(
            Currency currency, string exemptNode, int skip, int take, CancellationToken ct = default);

        /// <summary>User legs matching the query, newest first by default (balance history, event log).</summary>
        Task<PagedResult<LedgerLineDto>> SearchLinesAsync(LedgerQuery query, CancellationToken ct = default);

        /// <summary>What staff member <paramref name="actorUserId"/> added to players' balances of
        /// <paramref name="currency"/> through staff adjustments since <paramref name="since"/>,
        /// including the title bonuses their XP increases triggered (KNG-21: a TITLE_BONUS whose
        /// correlation id is one of those adjustments) - the per-staff daily grant cap, currency
        /// Phase 4. Call under the staff member's row lock so two grants can't both slip under the cap.</summary>
        Task<long> SumAdminGrantedSinceAsync(int actorUserId, Currency currency, DateTime since, CancellationToken ct = default);

        // Policy rows are admin-editable settings, not ledger rows (currency Phase 4).

        /// <summary>The currency's policy row, tracked for an edit; null if none.</summary>
        Task<CurrencyPolicy?> GetPolicyForUpdateAsync(Currency currency, CancellationToken ct = default);

        /// <summary>Saves changes made to a tracked policy row.</summary>
        Task SavePolicyAsync(CancellationToken ct = default);
    }
}
