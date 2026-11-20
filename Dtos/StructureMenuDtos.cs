using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    // In-game structure menus P6a (PandiO/knk-workspace#683, production DESIGN § 11.2, decision record § 15.1 M1-M5).
    // Public fields for every player; the detail only for members of the structure's town and staff (M3).

    /// <summary>Public status of a structure in the menus (M3): what every player may see.</summary>
    public static class StructureMenuStatus
    {
        public const string Working = "Working";
        public const string Stalled = "Stalled";
        public const string NoRoad = "NoRoad";
        public const string Full = "Full";
    }

    /// <summary>One row of the structures overview: public fields only.</summary>
    public class StructureMenuRowDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        /// <summary><c>ProductionStructure</c> or <c>Warehouse</c>.</summary>
        [JsonPropertyName("structureType")] public string StructureType { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("productionKindId")] public int? ProductionKindId { get; set; }
        /// <summary>The production kind's name; "Warehouse" for a warehouse.</summary>
        [JsonPropertyName("kindName")] public string? KindName { get; set; }
        [JsonPropertyName("townId")] public int? TownId { get; set; }
        [JsonPropertyName("townName")] public string? TownName { get; set; }
        [JsonPropertyName("isRural")] public bool IsRural { get; set; }
        /// <summary>The level (production structures); null for a warehouse.</summary>
        [JsonPropertyName("level")] public int? Level { get; set; }
        /// <summary>What it produces: the blueprint names of its lines.</summary>
        [JsonPropertyName("products")] public List<string> Products { get; set; } = new();
        /// <summary><see cref="StructureMenuStatus"/>: Working, Stalled, NoRoad or Full.</summary>
        [JsonPropertyName("status")] public string Status { get; set; } = StructureMenuStatus.Working;
        [JsonPropertyName("statusText")] public string StatusText { get; set; } = "";
    }

    /// <summary>
    /// The structure menu (M1-M3): the public row for everyone; <see cref="Detail"/> only when
    /// <see cref="ViewerSeesDetails"/> (members of the structure's town and staff). A visitor's response holds no stock,
    /// rates, workers or orders (A25).
    /// </summary>
    public class StructureMenuDto : StructureMenuRowDto
    {
        [JsonPropertyName("viewerSeesDetails")] public bool ViewerSeesDetails { get; set; }
        /// <summary>The viewer holds <c>knk.admin.world</c>: the menu shows the staff buttons (M4).</summary>
        [JsonPropertyName("viewerIsStaff")] public bool ViewerIsStaff { get; set; }
        [JsonPropertyName("detail")] public StructureMenuDetailDto? Detail { get; set; }
    }

    public class StructureMenuDetailDto
    {
        [JsonPropertyName("workersPresent")] public int? WorkersPresent { get; set; }
        [JsonPropertyName("workersTotal")] public int? WorkersTotal { get; set; }
        /// <summary>Workers carrying a transport order now (§ 7.7).</summary>
        [JsonPropertyName("workersAway")] public int? WorkersAway { get; set; }
        [JsonPropertyName("rates")] public List<StructureMenuRateDto> Rates { get; set; } = new();
        [JsonPropertyName("storages")] public List<StructureMenuStorageDto> Storages { get; set; } = new();
        [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = new();
        [JsonPropertyName("notProducingReasons")] public List<string> NotProducingReasons { get; set; } = new();
        [JsonPropertyName("openOrderCount")] public int OpenOrderCount { get; set; }
    }

    public class StructureMenuRateDto
    {
        [JsonPropertyName("productionLineId")] public int ProductionLineId { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("ratePerHour")] public decimal RatePerHour { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("notProducingReasons")] public List<string> NotProducingReasons { get; set; } = new();
    }

    /// <summary>A storage of the structure with its contents (V2 <c>StorageOverview</c>: "Amount: N" per item).</summary>
    public class StructureMenuStorageDto
    {
        [JsonPropertyName("storageId")] public int StorageId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("purpose")] public string Purpose { get; set; } = "";
        [JsonPropertyName("totalUnits")] public long TotalUnits { get; set; }
        [JsonPropertyName("maxUnits")] public long? MaxUnits { get; set; }
        [JsonPropertyName("lines")] public List<TerminalEntryDto> Lines { get; set; } = new();
    }
}
