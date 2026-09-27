using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// The title-bracket side of an XP change, shared by every path that changes XP through the
    /// ledger (staff adjustments, and siege rewards / discovery rewards once they post XP):
    /// resolves the bracket before and after, and on a promotion pays each crossed bracket's
    /// Coin/Gem/Exp bonus as a TITLE_BONUS ledger posting keyed
    /// <c>title-bonus:{userId}:{bracketId}</c> — once per user and bracket, ever (currency
    /// DESIGN.md D10; fixes audit A5, the demote/re-promote re-grant loop). A bracket paid to an
    /// account merged into the user's (ICurrencyService.GetMergedAccountIdsAsync) counts as paid,
    /// so an account merge can't collect a bracket's bonus a second time. An XP bonus can push
    /// into a further bracket, which is then crossed too. Demotions never claw anything back.
    /// Bonuses are scaled by the KNG-16 multipliers (coins by personal x rank salary, gems and XP
    /// by their own bonus multipliers). Writes one TitleChanged audit entry per change.
    /// <para>
    /// Call it right after the XP posting and inside the same transaction (the caller's
    /// IUserRepository.RunWithUsersLockedAsync), so the XP change and its bonuses commit
    /// together. Queuing the in-game notification stays with the caller, after its commit.
    /// </para>
    /// </summary>
    public interface ITitleProgressionService
    {
        /// <summary>
        /// Runs <see cref="ApplyAsync"/> for every user whose XP the posting changed, using the
        /// XP leg's BalanceBefore as the previous value. A replayed posting changed nothing, so it
        /// returns no changes. Keyed by user id; users whose title didn't change are absent.
        /// When the posting is a staff adjustment (ADMIN_GRANT/ADMIN_SET) by
        /// <paramref name="actorUserId"/>, the coin and gem bonuses count against that staff
        /// member's daily grant cap (KNG-21): over it, this throws AdminDailyCapExceeded and the
        /// caller's transaction must roll the XP change back with it.
        /// </summary>
        Task<Dictionary<int, TitleChangeResultDto>> ApplyForPostingAsync(PostingResult posting, int? actorUserId, CancellationToken ct = default);

        /// <summary>
        /// Title progression for a user whose XP just went from <paramref name="previousExperience"/>
        /// to its current (ledger-written) value. Null when there are no brackets or the bracket
        /// didn't change. <paramref name="correlationId"/> (optional) groups the bonus postings
        /// with the posting that caused them.
        /// </summary>
        Task<TitleChangeResultDto?> ApplyAsync(int userId, long previousExperience, int? actorUserId,
            string? correlationId = null, CancellationToken ct = default);
    }
}
