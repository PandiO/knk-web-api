using System;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// A title promotion or demotion, projected from an XP leg of the currency ledger (D9: every
/// title change is caused by XP). Durable — the TitleChanged audit rows are purged after 180 days.
/// Names are the gendered names at projection time; the history shows no reason (DESIGN.md
/// "Title history").
/// </summary>
public class PlayerTitleChange
{
    public long Id { get; set; }

    public int UserId { get; set; }

    public int? FromTitleBracketId { get; set; }

    public int ToTitleBracketId { get; set; }

    public string? FromTitleName { get; set; }

    public string ToTitleName { get; set; } = null!;

    public TitleChangeDirection Direction { get; set; }

    public long ExperienceBefore { get; set; }

    public long ExperienceAfter { get; set; }

    /// <summary>The ledger leg (currency_entries.Id) this change was projected from; unique, so
    /// re-projecting a leg never adds a second row.</summary>
    public long CurrencyEntryId { get; set; }

    /// <summary>The ledger transaction's CreatedAt.</summary>
    public DateTime ChangedAt { get; set; }
}
