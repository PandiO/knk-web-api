using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// A teleport-fee idempotency key that must never be charged (teleport DESIGN.md §3.7.3, KNG-17):
/// written when the plugin's refund for an attempt arrives before - or instead of - its charge
/// (the charge timed out, the plugin gave up and refunded the key). A charge with the key that
/// shows up later, even after an API restart or on another API instance, is refused.
/// <para>
/// Written and read under the player's row lock (IUserRepository.RunWithUsersLockedAsync), in the
/// same database transaction as the refund's ledger lookup, so a charge and a refund with one key
/// can't both win. Kept outside the append-only currency ledger, which only holds real postings.
/// The key is the primary key (binary collation, like the ledger's keys); rows are tiny and rare,
/// so they are kept.
/// </para>
/// </summary>
public class TeleportFeeVoid
{
    /// <summary>The plugin-scope idempotency key the refund voided.</summary>
    public string IdempotencyKey { get; set; } = null!;

    /// <summary>The player the refund named. No FK, like the ledger: it outlives a deleted account.</summary>
    public int UserId { get; set; }

    /// <summary>Why the plugin refunded (optional, at most 200 characters).</summary>
    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
