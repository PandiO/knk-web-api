namespace knkwebapi_v2.Enums;

/// <summary>
/// What a ledger entry moves (docs/specs/currency-payments/DESIGN.md §3.2). Experience points
/// share the ledger with the two currencies (KNG-23 folded into KNG-21, DESIGN.md §5 resolved
/// item 10), so every change to Coins, Gems and ExperiencePoints can be traced the same way.
/// Stored as a TINYINT; the numeric values are part of the ledger's history and must never be
/// renumbered.
/// </summary>
public enum Currency : byte
{
    Coins = 0,
    Gems = 1,
    Experience = 2
}
