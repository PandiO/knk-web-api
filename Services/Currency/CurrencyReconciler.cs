using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Proves the users balance columns agree with the ledger (docs/specs/currency-payments/
    /// DESIGN.md §3.9 rule R1/R2; used by tests now and by the currency monitor in Phase 5).
    /// <para>
    /// The ledger starts empty (DESIGN.md §5 resolved item 11: no opening-balance backfill), so
    /// instead of "column = Σ entries from zero" it checks, per user and currency that has ledger
    /// rows: every leg satisfies BalanceBefore + Amount = BalanceAfter; each leg's BalanceBefore
    /// equals the previous leg's BalanceAfter (nothing changed the balance outside the ledger in
    /// between); and the last BalanceAfter equals the users column. Together these give
    /// column = first BalanceBefore + Σ entries. Every transaction must also sum to zero per
    /// currency. Users without ledger rows aren't checked (their balance predates the ledger).
    /// </para>
    /// Note: until currency Phase 2 routes every write path through ICurrencyService, a balance
    /// changed by a pre-ledger path (salary, kits, admin adjust…) after a user's first ledger
    /// posting shows up here as a Chain/BalanceColumn mismatch — that is the reconciler working.
    /// </summary>
    public class CurrencyReconciler
    {
        private readonly KnKDbContext _context;

        public CurrencyReconciler(KnKDbContext context)
        {
            _context = context;
        }

        /// <summary>All mismatches (empty = reconciled), optionally for one user only.</summary>
        public async Task<List<CurrencyMismatchDto>> FindMismatchesAsync(int? userId = null, CancellationToken ct = default)
        {
            var mismatches = new List<CurrencyMismatchDto>();

            // R2: every transaction balances per currency.
            var unbalanced = await _context.CurrencyEntries.AsNoTracking()
                .Where(e => userId == null || _context.CurrencyEntries.Any(u => u.TransactionId == e.TransactionId && u.UserId == userId))
                .GroupBy(e => new { e.TransactionId, e.Currency })
                .Where(g => g.Sum(e => e.Amount) != 0)
                .Select(g => new { g.Key.TransactionId, g.Key.Currency, Sum = g.Sum(e => e.Amount) })
                .ToListAsync(ct);
            mismatches.AddRange(unbalanced.Select(u => new CurrencyMismatchDto
            {
                Kind = "UnbalancedTransaction",
                TransactionId = u.TransactionId,
                Currency = u.Currency.ToString(),
                Expected = 0,
                Actual = u.Sum
            }));

            // R1: the per-user chain and the users columns. Streamed in (user, currency, id) order,
            // so memory stays flat however long the ledger gets.
            var balances = await _context.Users.AsNoTracking()
                .Where(u => userId == null || u.Id == userId)
                .Select(u => new { u.Id, u.Coins, u.Gems, u.ExperiencePoints })
                .ToDictionaryAsync(u => u.Id, ct);

            var legs = _context.CurrencyEntries.AsNoTracking()
                .Where(e => e.AccountKind == CurrencyAccountKind.User && (userId == null || e.UserId == userId))
                .OrderBy(e => e.UserId).ThenBy(e => e.Currency).ThenBy(e => e.Id)
                .Select(e => new { e.Id, e.TransactionId, UserId = e.UserId!.Value, e.Currency, e.Amount, e.BalanceBefore, e.BalanceAfter })
                .AsAsyncEnumerable();

            (int UserId, Currency Currency, long After)? previous = null;

            void CheckColumn((int UserId, Currency Currency, long After) last)
            {
                long? column = balances.TryGetValue(last.UserId, out var b)
                    ? last.Currency switch
                    {
                        Currency.Coins => b.Coins,
                        Currency.Gems => b.Gems,
                        _ => b.ExperiencePoints
                    }
                    : null;
                if (column != last.After)
                {
                    mismatches.Add(new CurrencyMismatchDto
                    {
                        Kind = "BalanceColumn",
                        UserId = last.UserId,
                        Currency = last.Currency.ToString(),
                        Expected = last.After,
                        Actual = column
                    });
                }
            }

            await foreach (var leg in legs.WithCancellation(ct))
            {
                var sameAccount = previous.HasValue && previous.Value.UserId == leg.UserId && previous.Value.Currency == leg.Currency;
                if (previous.HasValue && !sameAccount)
                {
                    CheckColumn(previous.Value);
                }

                if (leg.BalanceBefore + leg.Amount != leg.BalanceAfter)
                {
                    mismatches.Add(new CurrencyMismatchDto
                    {
                        Kind = "Arithmetic",
                        UserId = leg.UserId,
                        Currency = leg.Currency.ToString(),
                        Expected = leg.BalanceBefore + leg.Amount,
                        Actual = leg.BalanceAfter,
                        EntryId = leg.Id,
                        TransactionId = leg.TransactionId
                    });
                }
                if (sameAccount && leg.BalanceBefore != previous!.Value.After)
                {
                    mismatches.Add(new CurrencyMismatchDto
                    {
                        Kind = "Chain",
                        UserId = leg.UserId,
                        Currency = leg.Currency.ToString(),
                        Expected = previous.Value.After,
                        Actual = leg.BalanceBefore,
                        EntryId = leg.Id,
                        TransactionId = leg.TransactionId
                    });
                }
                previous = (leg.UserId, leg.Currency, leg.BalanceAfter ?? 0);
            }
            if (previous.HasValue)
            {
                CheckColumn(previous.Value);
            }

            return mismatches;
        }
    }
}
