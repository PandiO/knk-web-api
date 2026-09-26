using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// The one place that changes a player's Coins, Gems or ExperiencePoints through the ledger
    /// (docs/specs/currency-payments/DESIGN.md §3.3, KNG-23 folded in). Every posting:
    /// <list type="bullet">
    /// <item>runs in one DB transaction that first locks the users' rows (SELECT … FOR UPDATE,
    /// ascending id). If the caller already opened a transaction on the same KnKDbContext
    /// (e.g. to write a kit claim or lootbox row too), the posting enlists in it and doesn't
    /// commit — both commit or neither does;</item>
    /// <item>is idempotent: (ctx.IdempotencyScope, ctx.IdempotencyKey) is unique. Repeating a key
    /// with the same request returns the original result with <c>Replayed = true</c> and writes
    /// nothing; the same key with a different request throws IdempotencyKeyReuse. Refused
    /// postings aren't stored, so a retry is evaluated again;</item>
    /// <item>writes double entry: each user leg is balanced by the reason's system account, and
    /// user legs record BalanceBefore/BalanceAfter;</item>
    /// <item>checks amounts with checked long math: 1..cap per leg, balances within 0..cap
    /// (BalanceLimits: 999,999,999 coins, 999,999 gems, int.MaxValue XP).</item>
    /// </list>
    /// Refusals throw <see cref="CurrencyException"/> (an InvalidOperationException) with a
    /// <see cref="CurrencyErrorCode"/>. Callers must not catch InsufficientFunds and retry with a
    /// smaller amount.
    /// <para>
    /// XP postings change ExperiencePoints only; they don't run title progression (promotion
    /// bonuses, title change notifications). Until currency Phase 2 reroutes it, XP that should
    /// promote goes through UserService.AdjustBalancesAsync.
    /// </para>
    /// </summary>
    public interface ICurrencyService
    {
        /// <summary>
        /// Multi-leg posting for a Grant/Spend/Merge reason (e.g. siege rewards: one leg per
        /// participant and currency, key <c>siege-match:{id}</c>). Each (user, currency) appears at
        /// most once; Credit reasons take positive legs, Debit reasons negative ones.
        /// </summary>
        Task<PostingResult> PostAsync(IReadOnlyList<CurrencyLeg> legs, CurrencyContext ctx, CancellationToken ct = default);

        /// <summary>Mint: <paramref name="amount"/> (≥ 1) to the user from the reason's system account. Needs a Credit reason.</summary>
        Task<PostingResult> GrantAsync(int userId, Currency currency, long amount, CurrencyContext ctx, CancellationToken ct = default);

        /// <summary>Burn: <paramref name="amount"/> (≥ 1) from the user to the reason's system account. Needs a Debit reason; InsufficientFunds if short.</summary>
        Task<PostingResult> SpendAsync(int userId, Currency currency, long amount, CurrencyContext ctx, CancellationToken ct = default);

        /// <summary>
        /// Staff add/remove/set. ctx.ReasonCode must be <c>CurrencyReasons.ForAdminMode(req.Mode)</c>
        /// (ADMIN_GRANT/ADMIN_TAKE/ADMIN_SET), ctx.Reason (the staff member's explanation) is
        /// required, and the initiator must be a person or the plugin service. A Set whose target
        /// equals the current balance is still recorded (amount 0) so the action stays traceable.
        /// </summary>
        Task<PostingResult> AdminAdjustAsync(AdminAdjustRequest req, CurrencyContext ctx, CancellationToken ct = default);

        /// <summary>
        /// Posts the mirror of transaction <paramref name="transactionId"/> (reason REVERSAL, key
        /// usually <c>reverse:{transactionId}</c>, ctx.Reason required). Once per transaction
        /// (AlreadyReversed); a reversal itself can't be reversed (NotReversible). See
        /// <see cref="ReversalOptions"/> for balances that have since been spent.
        /// </summary>
        Task<PostingResult> ReverseAsync(long transactionId, ReversalOptions opts, CurrencyContext ctx, CancellationToken ct = default);

        /// <summary>Current balances; UserNotFound if the user doesn't exist.</summary>
        Task<BalancesDto> GetBalancesAsync(int userId, CancellationToken ct = default);

        /// <summary>User legs matching <paramref name="q"/>, paged (balance history / event log).</summary>
        Task<PagedResultDto<LedgerLineDto>> GetHistoryAsync(LedgerQuery q, CancellationToken ct = default);
    }
}
