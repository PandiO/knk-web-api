using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// The header of one stock ledger transaction (stock core P1, PandiO/knk-workspace#97, production DESIGN § 4.3).
/// Append-only: written once by <c>IStockService</c>, never updated; a correction is a new transaction with
/// <see cref="ReversesTransactionId"/> set. The 30-day roll-up (G3) replaces old <c>Production</c> and
/// <c>ProductionInput</c> transactions by <see cref="StockMoveRollup"/> rows with the same totals.
/// </summary>
[FormConfigurableEntity("StockTransaction")]
public class StockTransaction
{
    public const int ActorComponentMaxLength = 60;
    public const int SourceTypeMaxLength = 40;
    public const int SourceRefMaxLength = 64;
    public const int NoteMaxLength = 500;
    public const int IdempotencyKeyMaxLength = 100;

    public long Id { get; set; }

    public StockReason Reason { get; set; }

    public StockMoveMode Mode { get; set; }

    public StockActorKind ActorKind { get; set; }

    /// <summary>The person who asked (staff or player); no FK on purpose, like the currency ledger: the ledger keeps
    /// the id after the account is gone.</summary>
    public int? ActorUserId { get; set; }

    /// <summary>The system component or game server that asked (<c>web</c>, <c>plugin</c>, <c>StockReconciler</c>, …).</summary>
    [MaxLength(ActorComponentMaxLength)]
    public string? ActorComponent { get; set; }

    /// <summary>What the transaction belongs to (<c>ProductionCycle</c>, <c>TransportOrder</c>, <c>LootboxClaim</c>, …).</summary>
    [MaxLength(SourceTypeMaxLength)]
    public string? SourceType { get; set; }

    [MaxLength(SourceRefMaxLength)]
    public string? SourceRef { get; set; }

    /// <summary>Why, in words (required for admin adjustments).</summary>
    [MaxLength(NoteMaxLength)]
    public string? Note { get; set; }

    /// <summary>Unique when set: a repeated request with the same key applies once and replays the first result.</summary>
    [MaxLength(IdempotencyKeyMaxLength)]
    public string? IdempotencyKey { get; set; }

    /// <summary>SHA-256 of the request; the same key with a different request is refused instead of replayed.</summary>
    public string RequestHash { get; set; } = null!;

    /// <summary>The transaction this one undoes (a compensating correction); unique, so a transaction is reversed once.</summary>
    [RelatedEntityField(typeof(StockTransaction))]
    [NavigationPair(nameof(ReversesTransaction))]
    public long? ReversesTransactionId { get; set; }
    [RelatedEntityField(typeof(StockTransaction))]
    public StockTransaction? ReversesTransaction { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [RelatedEntityField(typeof(StockMove))]
    public ICollection<StockMove> Moves { get; set; } = new List<StockMove>();
}

/// <summary>
/// One line of a stock transaction (DESIGN § 4.3): <see cref="Amount"/> of a blueprint, or one unique item, from one
/// storage to another. <see cref="Reason"/> and <see cref="CreatedAt"/> repeat the header's, so the ledger can be
/// queried and rolled up per storage without a join. A check constraint keeps it either fungible or unique. Listed
/// on the transaction's page (view only).
/// </summary>
[FormConfigurableEntity("StockMove")]
public class StockMove
{
    public long Id { get; set; }

    public long TransactionId { get; set; }
    public StockTransaction Transaction { get; set; } = null!;

    /// <summary>The request line this move came from (0-based), so an idempotent replay reports per line.</summary>
    public int LineNo { get; set; }

    public int FromStorageId { get; set; }
    public Storage FromStorage { get; set; } = null!;

    public int ToStorageId { get; set; }
    public Storage ToStorage { get; set; } = null!;

    /// <summary>Set for a fungible move; null for a unique item.</summary>
    public int? ItemBlueprintId { get; set; }
    public ItemBlueprint? ItemBlueprint { get; set; }

    /// <summary>Set for a unique item (then <see cref="Amount"/> is 1).</summary>
    public long? ItemInstanceId { get; set; }
    public ItemInstance? ItemInstance { get; set; }

    /// <summary>Always positive.</summary>
    public long Amount { get; set; }

    public StockReason Reason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// The 30-day roll-up of the ledger (G3, DESIGN § 14 D10): <c>Production</c> and <c>ProductionInput</c> moves older
/// than 30 days, summed per day, reason, storage pair and blueprint. The reconciler counts these with the moves, so
/// totals are unchanged. Unique per (day, reason, from, to, blueprint); a later roll-up of the same day adds to it.
/// </summary>
public class StockMoveRollup
{
    public long Id { get; set; }

    /// <summary>The UTC day of the moves (time 00:00).</summary>
    public DateTime Day { get; set; }

    public StockReason Reason { get; set; }

    public int FromStorageId { get; set; }
    public Storage FromStorage { get; set; } = null!;

    public int ToStorageId { get; set; }
    public Storage ToStorage { get; set; } = null!;

    public int ItemBlueprintId { get; set; }
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    public long Amount { get; set; }

    /// <summary>How many moves this row replaces.</summary>
    public int MoveCount { get; set; }

    public DateTime RolledUpAt { get; set; } = DateTime.UtcNow;
}
