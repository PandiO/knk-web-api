using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Player transfers (/pay), their confirmation step, the sender's limits and the balance
    /// leaderboard (currency DESIGN.md §3.4–§3.6, IMPLEMENTATION_PLAN.md Phase 3). Implemented by
    /// CurrencyService with the same locking and idempotency as every other posting; kept apart
    /// from ICurrencyService so the features that only grant or spend (and their test fakes)
    /// don't have to know about transfers.
    /// <para>
    /// Every rule is checked under the sender's and recipient's row locks (ascending id), so two
    /// concurrent sends from one player can't overdraw or pass a daily cap together, and A→B
    /// racing B→A can't deadlock. Refused transfers throw <see cref="CurrencyException"/> and
    /// store nothing, so a retry is evaluated again.
    /// </para>
    /// </summary>
    public interface ICurrencyTransferService
    {
        /// <summary>
        /// Sends a transfer. ctx: reason PLAYER_TRANSFER, the client's Idempotency-Key, initiator
        /// = the sender as Player. Below the policy's ConfirmThreshold the money moves at once
        /// (status Completed); at or above it nothing moves and a pending transfer is returned
        /// (status PendingConfirmation), which replaces any other open one of the same sender.
        /// Repeating the key returns the same outcome (Replayed) without moving money again.
        /// </summary>
        Task<TransferResultDto> TransferAsync(TransferRequest request, CurrencyContext ctx, CancellationToken ct = default);

        /// <summary>
        /// Confirms the sender's pending transfer: every rule is checked again now, then the
        /// money moves (key <c>transfer-confirm:{publicId}</c> in ctx's scope, so a second
        /// confirm returns the first result). Not the sender's / unknown → PendingTransferNotFound;
        /// cancelled → PendingTransferClosed; past its window → PendingTransferExpired.
        /// </summary>
        Task<TransferResultDto> ConfirmTransferAsync(string pendingPublicId, int senderUserId, CurrencyContext ctx,
            bool bypassLimits = false, CancellationToken ct = default);

        /// <summary>Cancels the sender's pending transfer (repeatable); a confirmed one → PendingTransferClosed.</summary>
        Task<PendingTransferDto> CancelTransferAsync(string pendingPublicId, int senderUserId, CancellationToken ct = default);

        /// <summary>What the user may send now (policy, their last 24 h, cooldown, eligibility).</summary>
        Task<TransferLimitsDto> GetTransferLimitsAsync(int userId, Currency currency, CancellationToken ct = default);

        /// <summary>Richest active players of Coins or Gems, excluding transfer-locked accounts and knk.baltop.exempt holders.</summary>
        Task<LeaderboardDto> GetLeaderboardAsync(Currency currency, int page, int pageSize, CancellationToken ct = default);
    }
}
