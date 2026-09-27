using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Writes synthetic ledger transactions with a chosen CreatedAt for the currency monitor tests
/// (the real CurrencyService always stamps "now"). Each user leg is self-consistent
/// (BalanceBefore + Amount = BalanceAfter, both ≥ 0, so the MySQL CHECKs accept it) and the
/// system leg balances the posting; the per-user chain is not maintained, so don't reconcile
/// a database seeded this way.
/// </summary>
internal static class CurrencyLedgerSeed
{
    public const long Headroom = 10_000_000;

    public static CurrencyTransaction Tx(
        CurrencyTransactionKind kind,
        string reasonCode,
        DateTime createdAt,
        params (int UserId, Currency Currency, long Amount)[] legs) =>
        Tx(kind, reasonCode, createdAt, null, null, null, legs);

    public static CurrencyTransaction Tx(
        CurrencyTransactionKind kind,
        string reasonCode,
        DateTime createdAt,
        int? fromUserId,
        int? toUserId,
        int? initiatorUserId,
        params (int UserId, Currency Currency, long Amount)[] legs)
    {
        var tx = new CurrencyTransaction
        {
            PublicId = CurrencyIds.NewPublicId(),
            Kind = kind,
            ReasonCode = reasonCode,
            Reason = "seeded",
            IdempotencyScope = CurrencyIdempotencyScopes.System,
            IdempotencyKey = "seed:" + Guid.NewGuid().ToString("N"),
            RequestHash = new string('0', 64),
            Initiator = initiatorUserId.HasValue ? CurrencyInitiator.Admin : CurrencyInitiator.System,
            InitiatorUserId = initiatorUserId,
            InitiatorComponent = initiatorUserId.HasValue ? null : "Seed",
            FromUserId = fromUserId,
            ToUserId = toUserId,
            CreatedAt = createdAt
        };
        foreach (var (userId, currency, amount) in legs)
        {
            tx.Entries.Add(new CurrencyEntry
            {
                Currency = currency,
                AccountKind = CurrencyAccountKind.User,
                UserId = userId,
                Operation = amount < 0 ? CurrencyOperation.Remove : CurrencyOperation.Add,
                Amount = amount,
                BalanceBefore = Headroom,
                BalanceAfter = Headroom + amount
            });
        }
        foreach (var group in legs.GroupBy(l => l.Currency))
        {
            var sum = group.Sum(l => l.Amount);
            if (sum != 0)
            {
                tx.Entries.Add(new CurrencyEntry
                {
                    Currency = group.Key,
                    AccountKind = CurrencyAccountKind.System,
                    SystemAccount = "SYS_TEST",
                    Operation = sum > 0 ? CurrencyOperation.Remove : CurrencyOperation.Add,
                    Amount = -sum
                });
            }
        }
        return tx;
    }

    /// <summary>A player transfer: sender -amount, recipient +amount.</summary>
    public static CurrencyTransaction Transfer(int from, int to, long amount, DateTime at) =>
        Tx(CurrencyTransactionKind.Transfer, CurrencyReasons.PlayerTransfer, at, from, to, null,
            (from, Currency.Coins, -amount), (to, Currency.Coins, amount));

    public static async Task AddAsync(KnKDbContext ctx, params CurrencyTransaction[] transactions)
    {
        ctx.CurrencyTransactions.AddRange(transactions);
        await ctx.SaveChangesAsync();
    }
}
