using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// One reward or grant handed to a player through <c>IItemDeliveryService</c> (P7, PandiO/knk-workspace#60, production
/// DESIGN § 4.6, decision record § 2.4). The game server places what the player can receive right away; everything else
/// comes here and moves <c>SYS_WORLD</c> → the player's Inbox storage (<see cref="StockReason.RewardInbox"/>). What does
/// not fit under the inbox's hard cap moves <c>SYS_WORLD</c> → <c>SYS_LOSS</c> (<see cref="StockReason.InboxOverflow"/>);
/// the ledger keeps both. One row per <see cref="IdempotencyKey"/> (unique), so a retried delivery applies once.
/// <para>
/// The <see cref="Lines"/> are the inbox lots: what each line put into the inbox, and how much of it was claimed,
/// expired or is still there. They carry the 30-day expiry and the reminders (D7); the stock line in the inbox storage
/// stays the truth for amounts (claims and expiry never move more than the storage holds).
/// </para>
/// </summary>
[FormConfigurableEntity("ItemDelivery")]
public class ItemDelivery
{
    public const int KeyMaxLength = 100;
    public const int SourceMaxLength = 64;
    public const int NoteMaxLength = 500;
    public const int HashLength = 64;

    public long Id { get; set; }

    [NavigationPair(nameof(User))]
    [RelatedEntityField(typeof(User))]
    public int UserId { get; set; }
    [RelatedEntityField(typeof(User))]
    public User User { get; set; } = null!;

    public ItemDeliveryReason Reason { get; set; } = ItemDeliveryReason.Other;

    public ItemDeliveryStatus Status { get; set; } = ItemDeliveryStatus.Inboxed;

    /// <summary>What the delivery is for, e.g. <c>KitClaim</c> / <c>LootboxClaim</c> and its id.</summary>
    [MaxLength(SourceMaxLength)]
    public string? SourceType { get; set; }

    [MaxLength(SourceMaxLength)]
    public string? SourceRef { get; set; }

    [MaxLength(NoteMaxLength)]
    public string? Note { get; set; }

    public StockActorKind ActorKind { get; set; } = StockActorKind.PluginServer;

    public int? ActorUserId { get; set; }

    [MaxLength(KeyMaxLength)]
    public string IdempotencyKey { get; set; } = null!;

    /// <summary>SHA-256 of the request; the same key with another request is refused (409).</summary>
    [MaxLength(HashLength)]
    public string RequestHash { get; set; } = null!;

    [NavigationPair(nameof(InboxStorage))]
    [RelatedEntityField(typeof(Storage))]
    public int InboxStorageId { get; set; }
    [RelatedEntityField(typeof(Storage))]
    public Storage InboxStorage { get; set; } = null!;

    /// <summary>The <see cref="StockReason.RewardInbox"/> transaction; null when nothing fitted.</summary>
    public long? StockTransactionId { get; set; }

    /// <summary>The <see cref="StockReason.InboxOverflow"/> transaction; null when nothing was lost.</summary>
    public long? OverflowTransactionId { get; set; }

    /// <summary>The inbox's hard cap (raw units × storage weight) when the delivery arrived.</summary>
    public long CapUnits { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When what is still in the inbox from this delivery moves to <c>SYS_LOSS</c> (30 days, D7).</summary>
    public DateTime ExpiresAt { get; set; }

    [RelatedEntityField(typeof(ItemDeliveryLine))]
    public ICollection<ItemDeliveryLine> Lines { get; set; } = new List<ItemDeliveryLine>();
}

/// <summary>
/// One line of an <see cref="ItemDelivery"/>, and the inbox lot it left: <see cref="Requested"/> =
/// <see cref="Inboxed"/> + <see cref="Lost"/>; <see cref="Remaining"/> = <see cref="Inboxed"/> − <see cref="Claimed"/> −
/// <see cref="Expired"/>. Claims take the lots that expire first. View only (listed on the delivery).
/// </summary>
[FormConfigurableEntity("ItemDeliveryLine")]
public class ItemDeliveryLine
{
    public long Id { get; set; }

    public long ItemDeliveryId { get; set; }
    public ItemDelivery ItemDelivery { get; set; } = null!;

    public int LineNo { get; set; }

    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    /// <summary>Set for a unique item (a <see cref="StockUnit"/>, amount 1); null for a stock line amount.</summary>
    [NavigationPair(nameof(ItemInstance))]
    [RelatedEntityField(typeof(ItemInstance))]
    public long? ItemInstanceId { get; set; }
    [RelatedEntityField(typeof(ItemInstance))]
    public ItemInstance? ItemInstance { get; set; }

    public long Requested { get; set; }
    public long Inboxed { get; set; }
    public long Lost { get; set; }
    public long Claimed { get; set; }
    public long Expired { get; set; }
    public long Remaining { get; set; }

    /// <summary>Copied from the delivery, so the sweep reads lots without a join.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>How many expiry reminders went out for this lot (one per configured day before expiry).</summary>
    public int RemindersSent { get; set; }
}
