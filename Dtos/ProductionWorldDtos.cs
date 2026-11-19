using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    // P2 world (PandiO/knk-workspace#91, production DESIGN § 3.3). Both subtypes carry every Structure field
    // (StructureDto), including the placement: in a district (districtId + streetId + houseNumber) or rural (townId).

    /// <summary>A warehouse: <c>api/Warehouses</c>. On create/update a null field means "keep" (create: the default).</summary>
    public class WarehouseDto : StructureDto
    {
        [JsonPropertyName("acceptsDeliveries")]
        public bool? AcceptsDeliveries { get; set; }

        [JsonPropertyName("priority")]
        public int? Priority { get; set; }

        /// <summary>The delivery storage, created with the warehouse. On update it may point at another storage the
        /// warehouse holds; null keeps it.</summary>
        [JsonPropertyName("defaultStorageId")]
        public int? DefaultStorageId { get; set; }

        /// <summary>Read only: the storages the warehouse holds (Kind Domain).</summary>
        [JsonPropertyName("storages")]
        public List<StructureStorageNavDto>? Storages { get; set; }
    }

    /// <summary>A production structure: <c>api/ProductionStructures</c>. On create/update a null field means "keep"
    /// (create: the default).</summary>
    public class ProductionStructureDto : StructureDto
    {
        [JsonPropertyName("productionKindId")]
        public int? ProductionKindId { get; set; }

        [JsonPropertyName("level")]
        public int? Level { get; set; }

        [JsonPropertyName("productionEnabled")]
        public bool? ProductionEnabled { get; set; }

        [JsonPropertyName("automaticTransport")]
        public bool? AutomaticTransport { get; set; }

        /// <summary>0 clears it (planner default).</summary>
        [JsonPropertyName("transportThreshold")]
        public int? TransportThreshold { get; set; }

        /// <summary>0 clears it (planner default).</summary>
        [JsonPropertyName("transportCapacity")]
        public int? TransportCapacity { get; set; }

        /// <summary>"Nearest" or "Preferred".</summary>
        [JsonPropertyName("warehousePreference")]
        public string? WarehousePreference { get; set; }

        /// <summary>0 clears it.</summary>
        [JsonPropertyName("preferredWarehouseId")]
        public int? PreferredWarehouseId { get; set; }

        [JsonPropertyName("preferredWarehouse")]
        public StructureStorageNavDto? PreferredWarehouse { get; set; }

        /// <summary>Read only here ("Unmeasured", "Connected", "Unconnected"): set through
        /// <c>PUT api/ProductionStructures/{id}/road-connection</c> (the game server's lane measurement).</summary>
        [JsonPropertyName("roadConnection")]
        public string? RoadConnection { get; set; }

        [JsonPropertyName("roadConnectionMeasuredAt")]
        public System.DateTime? RoadConnectionMeasuredAt { get; set; }

        [JsonPropertyName("workersTotal")]
        public int? WorkersTotal { get; set; }

        /// <summary>Read only (P3/P5 write it).</summary>
        [JsonPropertyName("workersPresent")]
        public int? WorkersPresent { get; set; }

        /// <summary>Read only: dashboard warnings (no road connection, contested or outside territory).</summary>
        [JsonPropertyName("warnings")]
        public List<string>? Warnings { get; set; }

        /// <summary>Read only: the storages the structure holds (Kind Domain).</summary>
        [JsonPropertyName("storages")]
        public List<StructureStorageNavDto>? Storages { get; set; }
    }

    /// <summary>A storage or warehouse row on a structure page.</summary>
    public class StructureStorageNavDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>Storages only: the purpose ("Output", "Delivery", ...).</summary>
        [JsonPropertyName("purpose")]
        public string? Purpose { get; set; }
    }

    /// <summary>The road connection measured by the game server (P5b, DESIGN § 7.6).</summary>
    public class RoadConnectionUpdateDto
    {
        /// <summary>"Unmeasured", "Connected" or "Unconnected".</summary>
        [JsonPropertyName("roadConnection")]
        public string? RoadConnection { get; set; }
    }

    /// <summary>Search row for warehouses and production structures (the structure row plus the subtype's columns).</summary>
    public class ProductionWorldListDto : StructureListDto
    {
        [JsonPropertyName("acceptsDeliveries")]
        public bool? acceptsDeliveries { get; set; }

        [JsonPropertyName("priority")]
        public int? priority { get; set; }

        [JsonPropertyName("productionKindId")]
        public int? productionKindId { get; set; }

        [JsonPropertyName("level")]
        public int? level { get; set; }

        [JsonPropertyName("productionEnabled")]
        public bool? productionEnabled { get; set; }

        [JsonPropertyName("roadConnection")]
        public string? roadConnection { get; set; }
    }
}
