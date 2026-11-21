using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Postings between players and a town's account (currency <see cref="CurrencyAccountKind.Town"/>, shops P10,
    /// PandiO/knk-workspace#248, TOWN_LOGISTICS D-L5): shop purchases pay the town, buy-backs are paid by it. Additive: no
    /// existing reason, posting or balance changes. Trade, tax and spending from town accounts stay Later (#124).
    /// </summary>
    public interface ICurrencyTownService
    {
        /// <summary>
        /// One posting whose user legs are balanced by a single town leg (the town's account gets −Σ user legs). Only the
        /// town reasons (<see cref="CurrencyReasons.IsTownReason"/>): <see cref="CurrencyReasons.ShopPurchase"/> takes
        /// negative user legs, <see cref="CurrencyReasons.ShopBuyBack"/> positive ones. The town's account is created on
        /// first use and locked with the users; it never goes below zero (<see cref="CurrencyErrorCode.InsufficientFunds"/>
        /// with <c>townId</c> in the details). Idempotent like every posting; runs inside the caller's database transaction
        /// when there is one.
        /// </summary>
        Task<PostingResult> PostWithTownAsync(int townId, IReadOnlyList<CurrencyLeg> legs, CurrencyContext ctx, CancellationToken ct = default);
    }

    public partial class CurrencyService : ICurrencyTownService
    {
        public Task<PostingResult> PostWithTownAsync(int townId, IReadOnlyList<CurrencyLeg> legs, CurrencyContext ctx,
            CancellationToken ct = default) =>
            ObserveAsync(() => PostWithTownCoreAsync(townId, legs, ctx, ct), ctx);

        private async Task<PostingResult> PostWithTownCoreAsync(int townId, IReadOnlyList<CurrencyLeg> legs, CurrencyContext ctx,
            CancellationToken ct)
        {
            var reason = RequireReason(ctx);
            if (!CurrencyReasons.IsTownReason(reason.Code))
            {
                throw Invalid($"{reason.Code} isn't paid to or from a town's account.");
            }
            ValidateContext(ctx, reason);
            if (townId <= 0)
            {
                throw Invalid("A town posting needs a town.");
            }
            if (legs == null || legs.Count == 0)
            {
                throw Invalid("A posting needs at least one leg.");
            }
            if (legs.GroupBy(l => (l.UserId, l.Currency)).Any(g => g.Count() > 1))
            {
                throw Invalid("Each user and currency may appear only once in a posting.");
            }
            foreach (var leg in legs)
            {
                if (leg.UserId <= 0)
                {
                    throw new CurrencyException(CurrencyErrorCode.UserNotFound, $"User {leg.UserId} doesn't exist.");
                }
                if (leg.Currency != Currency.Coins)
                {
                    throw Invalid("A town's account holds coins only.");
                }
                RequireAmountInRange(leg.Currency, leg.Amount == long.MinValue ? long.MaxValue : Math.Abs(leg.Amount), allowZero: false);
                if (reason.Direction == CurrencyReasonDirection.Credit && leg.Amount < 0
                    || reason.Direction == CurrencyReasonDirection.Debit && leg.Amount > 0)
                {
                    throw new CurrencyException(CurrencyErrorCode.AmountOutOfRange,
                        $"{reason.Code} only {(reason.Direction == CurrencyReasonDirection.Credit ? "adds to" : "takes from")} balances (leg for user {leg.UserId}: {leg.Amount}).");
                }
            }

            var canonical = "town|" + townId + "|" + reason.Code + "|" + string.Join(";", legs
                .OrderBy(l => l.UserId).ThenBy(l => l.Currency)
                .Select(l => $"{l.UserId}:{(int)l.Currency}:{l.Amount}"))
                + $"|source:{ctx.SourceType}:{ctx.SourceRef}";

            await _repo.EnsureTownAccountAsync(townId, ct);
            TownAccount? account = null;
            long accountBefore = 0;
            try
            {
                return await ExecuteAsync(ctx, reason, legs.Select(l => l.UserId), canonical, async (users, token) =>
                {
                    // Users first (ExecuteAsync), then the town: every town posting locks in this order.
                    account = await _repo.GetTownAccountForUpdateAsync(townId, token)
                        ?? throw new CurrencyException(CurrencyErrorCode.UserNotFound, $"Town {townId} has no account.");
                    accountBefore = account.Coins;
                    var plan = new EntryPlan();
                    foreach (var leg in legs)
                    {
                        plan.AddUserLeg(users[leg.UserId], leg.Currency, leg.Amount, leg.Amount < 0 ? CurrencyOperation.Remove : CurrencyOperation.Add);
                    }
                    long townAmount = -legs.Sum(l => l.Amount);
                    long after;
                    try
                    {
                        after = checked(accountBefore + townAmount);
                    }
                    catch (OverflowException)
                    {
                        throw new CurrencyException(CurrencyErrorCode.BalanceCapExceeded, $"Town {townId}'s account can't hold more coins.");
                    }
                    if (after < 0)
                    {
                        throw new CurrencyException(CurrencyErrorCode.InsufficientFunds,
                            $"Town {townId}'s account has {accountBefore:N0} coins, {-townAmount:N0} needed.",
                            new { townId, currency = Currency.Coins.ToString(), balance = accountBefore, required = -townAmount });
                    }
                    plan.AddEntry(new CurrencyEntry
                    {
                        Currency = Currency.Coins,
                        AccountKind = CurrencyAccountKind.Town,
                        TownId = townId,
                        Operation = townAmount < 0 ? CurrencyOperation.Remove : CurrencyOperation.Add,
                        Amount = townAmount,
                        BalanceBefore = accountBefore,
                        BalanceAfter = after,
                    });
                    account.Coins = after;
                    account.UpdatedAt = DateTime.UtcNow;
                    return plan.ToDraft();
                }, ct);
            }
            catch
            {
                // A refused or failed posting must not leave the account's new balance on a later SaveChanges.
                if (account != null)
                {
                    account.Coins = accountBefore;
                    _repo.DiscardTownAccount(account);
                }
                throw;
            }
        }
    }
}
