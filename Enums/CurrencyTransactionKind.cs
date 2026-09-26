namespace knkwebapi_v2.Enums;

/// <summary>
/// The shape of a ledger transaction (currency DESIGN.md §3.2), decided by its reason code
/// (Services/Currency/CurrencyReasons.cs). Stored as a TINYINT; never renumber.
/// </summary>
public enum CurrencyTransactionKind : byte
{
    /// <summary>A system account pays users (salary, rewards, bonuses).</summary>
    Grant = 0,

    /// <summary>Users pay a system account (kit costs, fees, purchases).</summary>
    Spend = 1,

    /// <summary>User to user (/pay, currency Phase 3).</summary>
    Transfer = 2,

    /// <summary>Staff add, remove or set a balance (ADMIN_GRANT / ADMIN_TAKE / ADMIN_SET).</summary>
    AdminAdjust = 3,

    /// <summary>Mirrors an earlier transaction's entries. A transaction is reversed at most once.</summary>
    Reversal = 4,

    /// <summary>Account merge: the secondary account's balance is forfeited (DESIGN.md §5 Q6).</summary>
    Merge = 5
}

/// <summary>
/// What one ledger entry did to its account (KNG-23): added, removed, or set the balance to a
/// target value. A Set entry's Amount is the delta the server computed under the row lock, so
/// BalanceBefore + Amount = BalanceAfter holds for every user entry. Stored as a TINYINT.
/// </summary>
public enum CurrencyOperation : byte
{
    Add = 0,
    Remove = 1,
    Set = 2
}

/// <summary>Which side of the double entry an entry belongs to. Stored as a TINYINT.</summary>
public enum CurrencyAccountKind : byte
{
    /// <summary>A player's balance (users.Coins/Gems/ExperiencePoints).</summary>
    User = 0,

    /// <summary>A system account such as SYS_SALARY: the source of minted and sink of burned amounts.</summary>
    System = 1
}
