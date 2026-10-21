using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Arena duel matches (arena duels DESIGN.md §4.3, §6.5, §7, §10.3; KNG-213): lock-in with the coin stakes
    /// escrowed in SYS_DUEL, the lifecycle the plugin checkpoints, settlement with review tiers, staff review
    /// actions and the history read. Every write is idempotent: a retry returns the stored result
    /// (<c>AlreadyDone</c>), and a call that contradicts the stored state is a DuelException WrongState (409).
    /// Refusals are <see cref="Duel.DuelException"/>s.
    /// </summary>
    public interface IDuelMatchService
    {
        /// <summary>The create rules without writing anything (invite and accept pre-checks).</summary>
        Task<DuelCheckResultDto> CheckAsync(DuelMatchCreateDto dto, CancellationToken ct = default);

        /// <summary>
        /// Lock-in: eligibility and limits, then the match row and one DUEL_STAKE posting with a debit leg
        /// per staking side (key <c>duel-lock:{token}</c>), all or nothing. The same token again returns the
        /// stored match (<c>created</c> false); the same token with a different body is TokenReused.
        /// </summary>
        Task<(DuelMatchDto Match, bool Created)> CreateAsync(DuelMatchCreateDto dto, CancellationToken ct = default);

        /// <summary>Created → InProgress on a battleground of the match's arena; a repeat returns the match.</summary>
        Task<DuelMatchDto> StartFightAsync(int id, DuelStartFightDto dto, CancellationToken ct = default);

        /// <summary>A Win or Forfeit: scores the review, then pays (tier 0/1) or holds (tier 2).</summary>
        Task<DuelResultDto> CompleteAsync(int id, DuelMatchCompleteDto dto, CancellationToken ct = default);

        /// <summary>A draw, cancel or abort: reverses the stake once, no fee.</summary>
        Task<DuelResultDto> CancelAsync(int id, DuelMatchCancelDto dto, CancellationToken ct = default);

        /// <summary>Startup recovery: every Created/InProgress match is aborted and refunded; held results stay.</summary>
        Task<DuelAbortUnfinishedResultDto> AbortUnfinishedAsync(DuelEndReason reason, CancellationToken ct = default);

        /// <summary>Held → paid (Released).</summary>
        Task<DuelResultDto> ReleaseAsync(int id, int? actorUserId, string? reason, CancellationToken ct = default);

        /// <summary>Held → both stakes refunded, spoils back to the loser, no fee (Voided).</summary>
        Task<DuelResultDto> VoidAsync(int id, int? actorUserId, string reason, CancellationToken ct = default);

        /// <summary>
        /// A paid result undone (DESIGN §10.3): the payout reversed with AllowPartial; revert also reverses the
        /// fee and returns the recovered coins to the loser first, then the winner; confiscate sends them to the
        /// SYS_FEES sink and keeps the fee. Review = Voided, audited.
        /// </summary>
        Task<DuelResultDto> RevertAsync(int id, int? actorUserId, string reason, bool confiscate, CancellationToken ct = default);

        Task<DuelMatchDto?> GetByIdAsync(int id, CancellationToken ct = default);

        Task<DuelMatchDto?> GetByTokenAsync(string publicToken, CancellationToken ct = default);

        /// <summary>Newest first; every filter optional.</summary>
        Task<List<DuelMatchDto>> QueryAsync(int? userId, int? arenaId, DuelMatchStatus? status, DuelReviewState? review,
            int limit, CancellationToken ct = default);

        /// <summary>Acknowledges the open DUEL_FLAG alerts of a pair or a beneficiary, so they stop counting.</summary>
        Task<DuelEscalationClearResultDto> ClearEscalationAsync(DuelEscalationClearDto dto, int? actorUserId, CancellationToken ct = default);

        Task<DuelPolicyDto> GetPolicyAsync(CancellationToken ct = default);

        Task<DuelPolicyDto> UpdatePolicyAsync(DuelPolicyDto dto, int? actorUserId, CancellationToken ct = default);

        Task<List<DuelRestrictionDto>> GetRestrictionsAsync(int? userId, bool activeOnly, CancellationToken ct = default);

        Task<DuelRestrictionDto> CreateRestrictionAsync(DuelRestrictionCreateDto dto, int? actorUserId, CancellationToken ct = default);

        Task<DuelRestrictionDto> LiftRestrictionAsync(int restrictionId, int? actorUserId, string? reason, CancellationToken ct = default);
    }
}
