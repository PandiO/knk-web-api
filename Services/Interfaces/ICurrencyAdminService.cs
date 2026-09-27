using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Staff currency tooling on top of the ledger (currency-payments IMPLEMENTATION_PLAN.md
    /// Phase 4): transaction detail, reversals, transfer locks and the per-currency policy. Every
    /// change is audit-logged. Staff balance adjustments go through
    /// IUserService.AdjustBalancesAsync (shared with PUT api/Users/{id}/balances). Refusals throw
    /// CurrencyException, ArgumentException (bad input) or KeyNotFoundException (unknown user).
    /// </summary>
    public interface ICurrencyAdminService
    {
        /// <summary>A transaction with every leg (user and system), usernames and reversal links;
        /// TransactionNotFound if none.</summary>
        Task<CurrencyTransactionDetailDto> GetTransactionAsync(string publicId, CancellationToken ct = default);

        /// <summary>
        /// Reverses <paramref name="publicId"/> for <paramref name="caller"/> (reason REVERSAL, key
        /// <c>reverse:{transactionId}</c>). Any request for a transaction that already has a
        /// reversal - a retry of the one that made it included - is refused with AlreadyReversed,
        /// whose details are an AlreadyReversedDetailsDto (which reversal, when, by whom), so staff
        /// never see a false "reversed". The note needs ≥ 10 characters. The posting, its audit
        /// entries and any title change from reversed XP commit together.
        /// </summary>
        Task<ReversalResultDto> ReverseAsync(string publicId, ReverseTransactionDto request, KnkCaller caller, string component, CancellationToken ct = default);

        Task<TransferLockDto> GetTransferLockAsync(int userId, CancellationToken ct = default);

        /// <summary>Locks the player's transfers (send and receive) with a reason; repeating it updates the reason.</summary>
        Task<TransferLockDto> SetTransferLockAsync(int userId, string reason, int? actorUserId, CancellationToken ct = default);

        /// <summary>Lifts the lock (no-op when not locked).</summary>
        Task<TransferLockDto> ClearTransferLockAsync(int userId, int? actorUserId, CancellationToken ct = default);

        /// <summary>The policy of each currency that has one (coins, gems).</summary>
        Task<List<CurrencyPolicyDto>> GetPoliciesAsync(CancellationToken ct = default);

        /// <summary>Validates and saves <paramref name="currency"/>'s policy; audit-logged with the changed fields.</summary>
        Task<CurrencyPolicyDto> UpdatePolicyAsync(Currency currency, CurrencyPolicyDto request, int? actorUserId, CancellationToken ct = default);
    }
}
