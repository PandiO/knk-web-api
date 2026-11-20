using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Dtos
{
    // Terminals and displays P6a (PandiO/knk-workspace#683, production DESIGN § 6). Enums travel as strings.

    /// <summary>A container bound to a storage, for read and write. On create and update the storage, mode, type,
    /// double flag, block position and label are read; <see cref="Slots"/> is derived from the type. On update a null
    /// field keeps the stored value.</summary>
    public class StorageContainerDto
    {
        [JsonPropertyName("id")] public int? Id { get; set; }
        [JsonPropertyName("storageId")] public int? StorageId { get; set; }
        [JsonPropertyName("storage")] public StockNavDto? Storage { get; set; }
        [JsonPropertyName("mode")] public StorageContainerMode? Mode { get; set; }
        [JsonPropertyName("containerType")] public StorageContainerType? ContainerType { get; set; }
        [JsonPropertyName("isDouble")] public bool? IsDouble { get; set; }
        [JsonPropertyName("slots")] public int? Slots { get; set; }
        [JsonPropertyName("worldName")] public string? WorldName { get; set; }
        [JsonPropertyName("x")] public int? X { get; set; }
        [JsonPropertyName("y")] public int? Y { get; set; }
        [JsonPropertyName("z")] public int? Z { get; set; }
        [JsonPropertyName("label")] public string? Label { get; set; }
        [JsonPropertyName("createdByUserId")] public int? CreatedByUserId { get; set; }
        [JsonPropertyName("createdAt")] public DateTime? CreatedAt { get; set; }
        [JsonPropertyName("updatedAt")] public DateTime? UpdatedAt { get; set; }

        // ---- Read only: the storage's container-derived capacity (DESIGN § 4.4) ----
        /// <summary>Σ slots of every container bound to the storage.</summary>
        [JsonPropertyName("storageContainerSlots")] public int? StorageContainerSlots { get; set; }
        [JsonPropertyName("storageUnitsPerSlot")] public int? StorageUnitsPerSlot { get; set; }
        /// <summary>The storage's cap after this write (slots × units per slot when units per slot is set).</summary>
        [JsonPropertyName("storageMaxUnits")] public long? StorageMaxUnits { get; set; }
    }

    public class StorageContainerListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("storageId")] public int StorageId { get; set; }
        [JsonPropertyName("storageName")] public string? StorageName { get; set; }
        [JsonPropertyName("mode")] public StorageContainerMode Mode { get; set; }
        [JsonPropertyName("containerType")] public StorageContainerType ContainerType { get; set; }
        [JsonPropertyName("isDouble")] public bool IsDouble { get; set; }
        [JsonPropertyName("slots")] public int Slots { get; set; }
        [JsonPropertyName("worldName")] public string WorldName { get; set; } = "";
        [JsonPropertyName("x")] public int X { get; set; }
        [JsonPropertyName("y")] public int Y { get; set; }
        [JsonPropertyName("z")] public int Z { get; set; }
        [JsonPropertyName("label")] public string? Label { get; set; }
    }

    /// <summary>One entry of a terminal page: a stock line (amount of a blueprint) or a unique item.</summary>
    public class TerminalEntryDto
    {
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; }
        /// <summary>Set for a unique item (amount 1); take it with this id.</summary>
        [JsonPropertyName("itemInstanceId")] public long? ItemInstanceId { get; set; }
        [JsonPropertyName("customDisplayName")] public string? CustomDisplayName { get; set; }
    }

    /// <summary>A terminal's view of its storage (Terminal mode): one page of entries, stock lines by name first, then
    /// unique items.</summary>
    public class TerminalViewDto
    {
        [JsonPropertyName("containerId")] public int ContainerId { get; set; }
        [JsonPropertyName("storageId")] public int StorageId { get; set; }
        [JsonPropertyName("storageName")] public string? StorageName { get; set; }
        [JsonPropertyName("totalUnits")] public long TotalUnits { get; set; }
        [JsonPropertyName("capacityUsed")] public decimal CapacityUsed { get; set; }
        [JsonPropertyName("maxUnits")] public long? MaxUnits { get; set; }
        [JsonPropertyName("isLocked")] public bool IsLocked { get; set; }
        [JsonPropertyName("page")] public int Page { get; set; }
        [JsonPropertyName("pageSize")] public int PageSize { get; set; }
        [JsonPropertyName("totalCount")] public int TotalCount { get; set; }
        [JsonPropertyName("entries")] public List<TerminalEntryDto> Entries { get; set; } = new();
    }

    /// <summary>Take from or put into a terminal's storage: an amount of a blueprint, or one unique item. The game server
    /// sends an idempotency key per click (a retry after lag applies once) and names the player in
    /// <c>X-Acting-User-Id</c>.</summary>
    public class TerminalMoveDto
    {
        [JsonPropertyName("itemBlueprintId")] public int? ItemBlueprintId { get; set; }
        [JsonPropertyName("itemInstanceId")] public long? ItemInstanceId { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; }
        [JsonPropertyName("idempotencyKey")] public string? IdempotencyKey { get; set; }
        /// <summary>Partial (default): a put moves what fits and reports the rest; AllOrNothing refuses a short move.</summary>
        [JsonPropertyName("mode")] public StockMoveMode? Mode { get; set; }
    }

    /// <summary>What a Display container shows: a read-only sample of the stock, proportional to the fill level.</summary>
    public class DisplaySampleDto
    {
        [JsonPropertyName("containerId")] public int ContainerId { get; set; }
        [JsonPropertyName("storageId")] public int StorageId { get; set; }
        [JsonPropertyName("slots")] public int Slots { get; set; }
        /// <summary>0..1: capacity used ÷ the storage's cap; 1 when the storage holds stock but has no cap.</summary>
        [JsonPropertyName("fillRatio")] public decimal FillRatio { get; set; }
        [JsonPropertyName("filledSlots")] public int FilledSlots { get; set; }
        /// <summary>Which blueprints fill the slots, largest stock first; Σ stacks = filled slots.</summary>
        [JsonPropertyName("items")] public List<DisplaySampleItemDto> Items { get; set; } = new();
    }

    public class DisplaySampleItemDto
    {
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("stacks")] public int Stacks { get; set; }
    }
}
