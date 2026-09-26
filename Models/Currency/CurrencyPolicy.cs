using System;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// Per-currency transfer and admin limits (currency DESIGN.md §3.5), one row per currency,
/// seeded with the §3.5 defaults by migration AddCurrencyLedger. Read by the transfer policy
/// (currency Phase 3) and the admin daily cap (Phase 4); nothing reads it in Phase 1. Balance
/// caps themselves stay in Services/BalanceLimits (mirrored by the users CHECK constraints);
/// MaxBalance here may only lower them.
/// </summary>
public class CurrencyPolicy
{
    public Currency Currency { get; set; }

    /// <summary>Kill switch for player transfers; flipped off by a critical reconciliation alert.</summary>
    public bool TransfersEnabled { get; set; } = true;

    /// <summary>Whether players may send this currency at all (gems: never, DESIGN.md §5 Q2).</summary>
    public bool Transferable { get; set; }

    public long MinTransfer { get; set; }

    public long MaxTransfer { get; set; }

    /// <summary>Rolling 24 h.</summary>
    public long DailySendCap { get; set; }

    /// <summary>Rolling 24 h.</summary>
    public long DailyReceiveCap { get; set; }

    /// <summary>Transfers of at least this amount need a confirmation step.</summary>
    public long ConfirmThreshold { get; set; }

    public int ConfirmTtlSeconds { get; set; } = 60;

    public int CooldownSeconds { get; set; }

    public int MaxTransfersPerHour { get; set; }

    public int MinSenderAccountAgeHours { get; set; }

    /// <summary>Lowest title bracket a sender must hold (1 = Peasant, DESIGN.md §5 Q5). Null = no title gate.</summary>
    public int? MinSenderTitleBracketId { get; set; }

    /// <summary>Fee on transfers in basis points (0 = none, DESIGN.md §5 Q3).</summary>
    public int TransferFeeBasisPoints { get; set; }

    public long MaxBalance { get; set; }

    /// <summary>What one staff member may grant per 24 h without knk.admin.currency.unlimited.</summary>
    public long AdminDailyGrantCapPerActor { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int? UpdatedByUserId { get; set; }
}
