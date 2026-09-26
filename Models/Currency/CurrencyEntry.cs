using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// One leg of a <see cref="CurrencyTransaction"/> (currency DESIGN.md §3.2, KNG-23). A user leg
/// records the balance before and after, so BalanceBefore + Amount = BalanceAfter and each
/// user's legs chain (a leg's BalanceBefore is the previous leg's BalanceAfter); the
/// reconciler (CurrencyReconciler) checks both, and that the last BalanceAfter equals the
/// users column. System legs have no balance. Append-only, like its header.
/// </summary>
public class CurrencyEntry
{
    public long Id { get; set; }

    public long TransactionId { get; set; }

    public CurrencyTransaction Transaction { get; set; } = null!;

    public Currency Currency { get; set; }

    public CurrencyAccountKind AccountKind { get; set; }

    /// <summary>Set when AccountKind = User. No FK to users (see CurrencyTransaction.InitiatorUserId).</summary>
    public int? UserId { get; set; }

    /// <summary>Set when AccountKind = System, e.g. SYS_SALARY (CurrencyReasons).</summary>
    public string? SystemAccount { get; set; }

    public CurrencyOperation Operation { get; set; }

    /// <summary>Signed change. Zero only for a Set that found the balance already at the target.</summary>
    public long Amount { get; set; }

    /// <summary>User legs only.</summary>
    public long? BalanceBefore { get; set; }

    /// <summary>User legs only.</summary>
    public long? BalanceAfter { get; set; }
}
