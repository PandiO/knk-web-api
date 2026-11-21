using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Dtos
{
    // ===== P7 item delivery, reward inbox, stash (PandiO/knk-workspace#60, production DESIGN § 4.6, D7) =====

    /// <summary>
    /// Inbox and stash rules (game settings, production section <c>delivery</c>; decision record § 2.4). Caps count raw
    /// units × <c>ItemBlueprint.StorageWeight</c>, like every storage cap (DESIGN § 4.4). A player's cap is the higher of
    /// the base and their active premium tier's entry in <see cref="PremiumTiers"/>. Null in stored JSON = the defaults;
    /// null on a save = keep the stored values.
    /// </summary>
    public class ProductionDeliverySettingsDto
    {
        public const long MaxCap = 10_000_000;

        /// <summary>The inbox hard cap without a premium tier. 2304 = a player inventory of 36 stacks of 64.</summary>
        [JsonPropertyName("inboxBaseCapUnits")]
        public long InboxBaseCapUnits { get; set; } = 2304;

        /// <summary>The stash cap without a premium tier. 0: the stash is a premium perk (vision § 9.1).</summary>
        [JsonPropertyName("stashBaseCapUnits")]
        public long StashBaseCapUnits { get; set; }

        /// <summary>Days an inbox item waits before it moves to <c>SYS_LOSS</c> (InboxExpired).</summary>
        [JsonPropertyName("expiryDays")]
        public int ExpiryDays { get; set; } = 30;

        /// <summary>A reminder goes out this many days before expiry, once per entry.</summary>
        [JsonPropertyName("reminderDaysBefore")]
        public List<int> ReminderDaysBefore { get; set; } = new() { 7, 1 };

        /// <summary>A near-cap warning goes out when a delivery takes the inbox past one of these fill levels.</summary>
        [JsonPropertyName("warnAtPercent")]
        public List<int> WarnAtPercent { get; set; } = new() { 80, 95 };

        [JsonPropertyName("premiumTiers")]
        public List<PremiumStorageCapDto> PremiumTiers { get; set; } = new();
    }

    /// <summary>The caps a premium tier (a permission group with <c>IsPremiumTier</c>) gives; null = the base.</summary>
    public class PremiumStorageCapDto
    {
        [JsonPropertyName("permissionGroupId")] public int PermissionGroupId { get; set; }
        [JsonPropertyName("inboxCapUnits")] public long? InboxCapUnits { get; set; }
        [JsonPropertyName("stashCapUnits")] public long? StashCapUnits { get; set; }
    }

    /// <summary>One line of a delivery: an amount of a blueprint, or one unique item (<c>itemInstanceId</c>).</summary>
    public class ItemDeliveryLineRequestDto
    {
        [JsonPropertyName("itemBlueprintId")] public int? ItemBlueprintId { get; set; }
        [JsonPropertyName("itemInstanceId")] public long? ItemInstanceId { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; }
    }

    /// <summary>
    /// <c>POST api/ItemDeliveries</c>: items for a player that the game server could not place (offline, full
    /// inventory, in a siege match or a minigame), or a grant from staff. The key is required: a retry with the same
    /// key and request applies once.
    /// </summary>
    public class ItemDeliveryRequestDto
    {
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("reason")] public ItemDeliveryReason Reason { get; set; } = ItemDeliveryReason.Other;
        [JsonPropertyName("sourceType")] public string? SourceType { get; set; }
        [JsonPropertyName("sourceRef")] public string? SourceRef { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
        [JsonPropertyName("idempotencyKey")] public string? IdempotencyKey { get; set; }
        [JsonPropertyName("lines")] public List<ItemDeliveryLineRequestDto> Lines { get; set; } = new();
    }

    public class ItemDeliveryLineDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("lineNo")] public int LineNo { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("itemInstanceId")] public long? ItemInstanceId { get; set; }
        [JsonPropertyName("customDisplayName")] public string? CustomDisplayName { get; set; }
        [JsonPropertyName("requested")] public long Requested { get; set; }
        [JsonPropertyName("inboxed")] public long Inboxed { get; set; }
        [JsonPropertyName("lost")] public long Lost { get; set; }
        [JsonPropertyName("claimed")] public long Claimed { get; set; }
        [JsonPropertyName("expired")] public long Expired { get; set; }
        [JsonPropertyName("remaining")] public long Remaining { get; set; }
        [JsonPropertyName("expiresAt")] public DateTime ExpiresAt { get; set; }
        [JsonPropertyName("remindersSent")] public int RemindersSent { get; set; }
    }

    /// <summary>A delivery and its lines (the detail page; also the answer to a delivery).</summary>
    public class ItemDeliveryDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("username")] public string? Username { get; set; }
        [JsonPropertyName("reason")] public ItemDeliveryReason Reason { get; set; }
        [JsonPropertyName("status")] public ItemDeliveryStatus Status { get; set; }
        [JsonPropertyName("sourceType")] public string? SourceType { get; set; }
        [JsonPropertyName("sourceRef")] public string? SourceRef { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
        [JsonPropertyName("actorKind")] public StockActorKind ActorKind { get; set; }
        [JsonPropertyName("actorUserId")] public int? ActorUserId { get; set; }
        [JsonPropertyName("idempotencyKey")] public string? IdempotencyKey { get; set; }
        [JsonPropertyName("inboxStorageId")] public int InboxStorageId { get; set; }
        [JsonPropertyName("stockTransactionId")] public long? StockTransactionId { get; set; }
        [JsonPropertyName("overflowTransactionId")] public long? OverflowTransactionId { get; set; }
        [JsonPropertyName("capUnits")] public long CapUnits { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("expiresAt")] public DateTime ExpiresAt { get; set; }
        [JsonPropertyName("inboxedUnits")] public long InboxedUnits { get; set; }
        [JsonPropertyName("lostUnits")] public long LostUnits { get; set; }
        [JsonPropertyName("remainingUnits")] public long RemainingUnits { get; set; }

        /// <summary>True when this answer is the stored result of an earlier request with the same key.</summary>
        [JsonPropertyName("replayed")] public bool Replayed { get; set; }

        [JsonPropertyName("lines")] public List<ItemDeliveryLineDto> Lines { get; set; } = new();
    }

    public class ItemDeliveryListDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("username")] public string? Username { get; set; }
        [JsonPropertyName("reason")] public ItemDeliveryReason Reason { get; set; }
        [JsonPropertyName("status")] public ItemDeliveryStatus Status { get; set; }
        [JsonPropertyName("sourceType")] public string? SourceType { get; set; }
        [JsonPropertyName("sourceRef")] public string? SourceRef { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("expiresAt")] public DateTime ExpiresAt { get; set; }
        [JsonPropertyName("inboxedUnits")] public long InboxedUnits { get; set; }
        [JsonPropertyName("lostUnits")] public long LostUnits { get; set; }
        [JsonPropertyName("remainingUnits")] public long RemainingUnits { get; set; }
    }

    /// <summary>One entry of a player storage view: a blueprint amount, or one unique item.</summary>
    public class PlayerStorageEntryDto
    {
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("itemInstanceId")] public long? ItemInstanceId { get; set; }
        [JsonPropertyName("customDisplayName")] public string? CustomDisplayName { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; }

        /// <summary>Inbox only: the first moment some of this entry expires; null for items without a delivery.</summary>
        [JsonPropertyName("nextExpiresAt")] public DateTime? NextExpiresAt { get; set; }

        /// <summary>Inbox only: how much expires at <see cref="NextExpiresAt"/>.</summary>
        [JsonPropertyName("nextExpiringAmount")] public long NextExpiringAmount { get; set; }
    }

    /// <summary>A player's Inbox or Stash (<c>GET api/Inbox/{userId}</c>, <c>.../stash</c>): what the menu shows.</summary>
    public class PlayerStorageViewDto
    {
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("purpose")] public StoragePurpose Purpose { get; set; }

        /// <summary>Null until the first delivery or deposit creates the storage.</summary>
        [JsonPropertyName("storageId")] public int? StorageId { get; set; }
        [JsonPropertyName("usedUnits")] public decimal UsedUnits { get; set; }
        [JsonPropertyName("capUnits")] public long CapUnits { get; set; }
        [JsonPropertyName("fillPercent")] public int FillPercent { get; set; }

        /// <summary>The highest warning level reached (one of <c>warnAtPercent</c>), or null.</summary>
        [JsonPropertyName("warningPercent")] public int? WarningPercent { get; set; }
        [JsonPropertyName("premiumTierGroupId")] public int? PremiumTierGroupId { get; set; }
        [JsonPropertyName("isLocked")] public bool IsLocked { get; set; }
        [JsonPropertyName("entries")] public List<PlayerStorageEntryDto> Entries { get; set; } = new();
    }

    /// <summary>
    /// <c>POST api/Inbox/{userId}/claim</c>, <c>.../stash/deposit</c>, <c>.../stash/withdraw</c>. The game server
    /// claims what the player has room for (partial space: less than the inbox holds); every click has its own key.
    /// </summary>
    public class PlayerStorageMoveDto
    {
        [JsonPropertyName("idempotencyKey")] public string? IdempotencyKey { get; set; }
        [JsonPropertyName("lines")] public List<ItemDeliveryLineRequestDto> Lines { get; set; } = new();
    }

    public class PlayerStorageMoveResultDto
    {
        [JsonPropertyName("storageId")] public int StorageId { get; set; }
        [JsonPropertyName("result")] public StockResultDto Result { get; set; } = new();

        /// <summary>The storage after the move.</summary>
        [JsonPropertyName("view")] public PlayerStorageViewDto View { get; set; } = new();
    }

    /// <summary>What a maintenance run did (<c>POST api/Inbox/sweep</c> and the hourly run).</summary>
    public class InboxSweepResultDto
    {
        [JsonPropertyName("remindersSent")] public int RemindersSent { get; set; }
        [JsonPropertyName("expiredLines")] public int ExpiredLines { get; set; }
        [JsonPropertyName("expiredUnits")] public long ExpiredUnits { get; set; }
        [JsonPropertyName("ranAt")] public DateTime RanAt { get; set; }
    }

    /// <summary>What an InboxUpdated player notification carries.</summary>
    public class InboxNotificationDto
    {
        public const string EventDelivered = "Delivered";
        public const string EventExpiryReminder = "ExpiryReminder";
        public const string EventExpired = "Expired";

        /// <summary><see cref="EventDelivered"/>, <see cref="EventExpiryReminder"/> or <see cref="EventExpired"/>.</summary>
        [JsonPropertyName("event")] public string Event { get; set; } = EventDelivered;
        [JsonPropertyName("deliveryId")] public long? DeliveryId { get; set; }
        [JsonPropertyName("reason")] public string? Reason { get; set; }
        [JsonPropertyName("inboxedUnits")] public long InboxedUnits { get; set; }

        /// <summary>Lost at the cap (InboxOverflow) or by expiry (InboxExpired); the loss notification when above 0.</summary>
        [JsonPropertyName("lostUnits")] public long LostUnits { get; set; }

        /// <summary>A near-cap warning: the fill level this delivery passed; null when it passed none.</summary>
        [JsonPropertyName("warningPercent")] public int? WarningPercent { get; set; }
        [JsonPropertyName("fillPercent")] public int FillPercent { get; set; }
        [JsonPropertyName("capUnits")] public long CapUnits { get; set; }

        /// <summary>Reminders: how much expires, and when the first of it does.</summary>
        [JsonPropertyName("expiringUnits")] public long ExpiringUnits { get; set; }
        [JsonPropertyName("expiresAt")] public DateTime? ExpiresAt { get; set; }
    }
}
