using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Dtos
{
    // Stock core P1 (PandiO/knk-workspace#97, production DESIGN § 4). Enums travel as strings.

    public class StockNavDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    /// <summary>
    /// A storage, for read and write. On create and update only name, kind, purpose, the holder id that matches the
    /// kind, capacity and lock are read; the totals, lines and units are written by the stock service only. Kind and
    /// holder change only while the storage has no stock and no history; on update a null kind or holder id keeps it. Send <see cref="RowVersion"/> to refuse an
    /// update over a newer version (409).
    /// </summary>
    public class StorageDto
    {
        [JsonPropertyName("id")] public int? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("kind")] public StorageKind? Kind { get; set; }
        [JsonPropertyName("purpose")] public StoragePurpose? Purpose { get; set; }

        [JsonPropertyName("domainId")] public int? DomainId { get; set; }
        [JsonPropertyName("domain")] public StockNavDto? Domain { get; set; }
        [JsonPropertyName("userId")] public int? UserId { get; set; }
        [JsonPropertyName("user")] public StockNavDto? User { get; set; }
        [JsonPropertyName("clanId")] public int? ClanId { get; set; }
        [JsonPropertyName("clan")] public StockNavDto? Clan { get; set; }
        [JsonPropertyName("systemKey")] public string? SystemKey { get; set; }
        /// <summary>The holder's name (domain, username, clan), the system key, or "Transit".</summary>
        [JsonPropertyName("holderName")] public string? HolderName { get; set; }

        [JsonPropertyName("maxUnits")] public long? MaxUnits { get; set; }
        [JsonPropertyName("maxDistinct")] public int? MaxDistinct { get; set; }
        [JsonPropertyName("unitsPerSlot")] public int? UnitsPerSlot { get; set; }

        [JsonPropertyName("totalUnits")] public long? TotalUnits { get; set; }
        [JsonPropertyName("distinctItems")] public int? DistinctItems { get; set; }
        /// <summary>Σ amount × storage weight, in raw units (DESIGN § 4.4); detail reads only.</summary>
        [JsonPropertyName("capacityUsed")] public decimal? CapacityUsed { get; set; }

        [JsonPropertyName("rowVersion")] public long? RowVersion { get; set; }
        [JsonPropertyName("lockedUntil")] public DateTime? LockedUntil { get; set; }
        [JsonPropertyName("lockReason")] public string? LockReason { get; set; }
        [JsonPropertyName("isLocked")] public bool? IsLocked { get; set; }
        [JsonPropertyName("createdAt")] public DateTime? CreatedAt { get; set; }

        [JsonPropertyName("lines")] public List<StockLineDto>? Lines { get; set; }
        [JsonPropertyName("units")] public List<StockUnitDto>? Units { get; set; }
    }

    public class StorageListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("kind")] public StorageKind Kind { get; set; }
        [JsonPropertyName("purpose")] public StoragePurpose Purpose { get; set; }
        [JsonPropertyName("holderName")] public string? HolderName { get; set; }
        [JsonPropertyName("domainId")] public int? DomainId { get; set; }
        [JsonPropertyName("userId")] public int? UserId { get; set; }
        [JsonPropertyName("clanId")] public int? ClanId { get; set; }
        [JsonPropertyName("systemKey")] public string? SystemKey { get; set; }
        [JsonPropertyName("totalUnits")] public long TotalUnits { get; set; }
        [JsonPropertyName("distinctItems")] public int DistinctItems { get; set; }
        [JsonPropertyName("maxUnits")] public long? MaxUnits { get; set; }
        [JsonPropertyName("maxDistinct")] public int? MaxDistinct { get; set; }
        [JsonPropertyName("lockedUntil")] public DateTime? LockedUntil { get; set; }
    }

    public class StockLineDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("storageId")] public int StorageId { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        /// <summary>The blueprint's id and name, for the Storage display seed's Stock section (#819).</summary>
        [JsonPropertyName("itemBlueprint")] public NamedRefDto? ItemBlueprint { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; }
        [JsonPropertyName("storageWeight")] public decimal StorageWeight { get; set; }
        [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; set; }
    }

    public class StockUnitDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("storageId")] public int StorageId { get; set; }
        [JsonPropertyName("itemInstanceId")] public long ItemInstanceId { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("customDisplayName")] public string? CustomDisplayName { get; set; }
        [JsonPropertyName("addedAt")] public DateTime AddedAt { get; set; }
    }

    // ---- Ledger ----

    public class StockTransactionListDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("reason")] public StockReason Reason { get; set; }
        [JsonPropertyName("mode")] public StockMoveMode Mode { get; set; }
        [JsonPropertyName("actorKind")] public StockActorKind ActorKind { get; set; }
        [JsonPropertyName("actorUserId")] public int? ActorUserId { get; set; }
        [JsonPropertyName("actorComponent")] public string? ActorComponent { get; set; }
        [JsonPropertyName("sourceType")] public string? SourceType { get; set; }
        [JsonPropertyName("sourceRef")] public string? SourceRef { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
        [JsonPropertyName("reversesTransactionId")] public long? ReversesTransactionId { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("moveCount")] public int MoveCount { get; set; }
        [JsonPropertyName("totalAmount")] public long TotalAmount { get; set; }
    }

    public class StockTransactionDto : StockTransactionListDto
    {
        [JsonPropertyName("actorUsername")] public string? ActorUsername { get; set; }
        [JsonPropertyName("idempotencyKey")] public string? IdempotencyKey { get; set; }
        /// <summary>The correction that undid this transaction, if any.</summary>
        [JsonPropertyName("reversedByTransactionId")] public long? ReversedByTransactionId { get; set; }
        [JsonPropertyName("moves")] public List<StockMoveDto> Moves { get; set; } = new();
    }

    public class StockMoveDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("transactionId")] public long TransactionId { get; set; }
        [JsonPropertyName("lineNo")] public int LineNo { get; set; }
        [JsonPropertyName("fromStorageId")] public int FromStorageId { get; set; }
        [JsonPropertyName("fromStorageName")] public string? FromStorageName { get; set; }
        [JsonPropertyName("toStorageId")] public int ToStorageId { get; set; }
        [JsonPropertyName("toStorageName")] public string? ToStorageName { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int? ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("itemInstanceId")] public long? ItemInstanceId { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; }
        [JsonPropertyName("reason")] public StockReason Reason { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
    }

    public class StockMoveRollupDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("day")] public DateTime Day { get; set; }
        [JsonPropertyName("reason")] public StockReason Reason { get; set; }
        [JsonPropertyName("fromStorageId")] public int FromStorageId { get; set; }
        [JsonPropertyName("fromStorageName")] public string? FromStorageName { get; set; }
        [JsonPropertyName("toStorageId")] public int ToStorageId { get; set; }
        [JsonPropertyName("toStorageName")] public string? ToStorageName { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; }
        [JsonPropertyName("moveCount")] public int MoveCount { get; set; }
        [JsonPropertyName("rolledUpAt")] public DateTime RolledUpAt { get; set; }
    }

    // ---- Requests and results ----

    public class StockLineRequestDto
    {
        [JsonPropertyName("fromStorageId")] public int FromStorageId { get; set; }
        [JsonPropertyName("toStorageId")] public int ToStorageId { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int? ItemBlueprintId { get; set; }
        [JsonPropertyName("itemInstanceId")] public long? ItemInstanceId { get; set; }
        /// <summary>Raw units; 1 for a unique item (may be left out then).</summary>
        [JsonPropertyName("amount")] public long? Amount { get; set; }
    }

    /// <summary>A manual move (staff) or a game-server move: one transaction with one reason.</summary>
    public class StockMoveRequestDto
    {
        [JsonPropertyName("reason")] public StockReason Reason { get; set; }
        [JsonPropertyName("mode")] public StockMoveMode Mode { get; set; } = StockMoveMode.AllOrNothing;
        [JsonPropertyName("lines")] public List<StockLineRequestDto> Lines { get; set; } = new();
        [JsonPropertyName("note")] public string? Note { get; set; }
        [JsonPropertyName("sourceType")] public string? SourceType { get; set; }
        [JsonPropertyName("sourceRef")] public string? SourceRef { get; set; }
        /// <summary>Recommended for every caller that may retry: the same key applies once.</summary>
        [JsonPropertyName("idempotencyKey")] public string? IdempotencyKey { get; set; }
    }

    /// <summary>The adjust dialog: + puts stock into the storage from SYS_ADJUSTMENT, − takes it out.</summary>
    public class StockAdjustRequestDto
    {
        [JsonPropertyName("storageId")] public int StorageId { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int? ItemBlueprintId { get; set; }
        [JsonPropertyName("itemInstanceId")] public long? ItemInstanceId { get; set; }
        [JsonPropertyName("delta")] public long Delta { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
        [JsonPropertyName("idempotencyKey")] public string? IdempotencyKey { get; set; }
    }

    public class StockReverseRequestDto
    {
        [JsonPropertyName("note")] public string? Note { get; set; }
        [JsonPropertyName("idempotencyKey")] public string? IdempotencyKey { get; set; }
    }

    public class StockLineResultDto
    {
        [JsonPropertyName("lineNo")] public int LineNo { get; set; }
        [JsonPropertyName("fromStorageId")] public int FromStorageId { get; set; }
        [JsonPropertyName("toStorageId")] public int ToStorageId { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int? ItemBlueprintId { get; set; }
        [JsonPropertyName("itemInstanceId")] public long? ItemInstanceId { get; set; }
        [JsonPropertyName("requested")] public long Requested { get; set; }
        [JsonPropertyName("accepted")] public long Accepted { get; set; }
        [JsonPropertyName("rejected")] public long Rejected { get; set; }
        [JsonPropertyName("rejectReason")] public StockRejectReason RejectReason { get; set; }
    }

    /// <summary>A stock request's result: capacity, missing stock and locks are results (200), not errors.</summary>
    public class StockResultDto
    {
        [JsonPropertyName("status")] public StockResultStatus Status { get; set; }
        [JsonPropertyName("transactionId")] public long? TransactionId { get; set; }
        [JsonPropertyName("replayed")] public bool Replayed { get; set; }
        [JsonPropertyName("accepted")] public long Accepted { get; set; }
        [JsonPropertyName("lines")] public List<StockLineResultDto> Lines { get; set; } = new();
    }
}
