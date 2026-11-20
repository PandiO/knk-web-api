using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    // ===== Territory V1 (PandiO/knk-workspace#326, knk-workspace docs/specs/territory/DESIGN.md) =====

    /// <summary>
    /// The production game settings (production DESIGN § 18.2, G16): the feature flag <c>Production:Enabled</c> and the
    /// territory range settings (territory DESIGN § 1, G11). Never null on read.
    /// </summary>
    public class ProductionSettingsDto
    {
        /// <summary><c>Production:Enabled</c>: gates the feature's background work (for territory: the game server's
        /// report). Admin CRUD stays available while it is off.</summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("territory")]
        public TerritorySettingsDto Territory { get; set; } = new();

        /// <summary>The production engine's timing (P3, PandiO/knk-workspace#86). Null in stored JSON = the defaults.</summary>
        [JsonPropertyName("engine")]
        public ProductionEngineSettingsDto Engine { get; set; } = new();

        /// <summary>Transport (P5, PandiO/knk-workspace#79). Null in stored JSON = the defaults; null on a save = keep the
        /// stored values (so a client that doesn't know the section yet doesn't reset it).</summary>
        [JsonPropertyName("transport")]
        public ProductionTransportSettingsDto? Transport { get; set; }
    }

    /// <summary>
    /// Transport timing and sizing (P5, production DESIGN § 7.4, § 7.6-7.7, G14). The simulated trip takes route length ÷
    /// <see cref="WalkingSpeedBlocksPerSecond"/>; without a measured lane the route is the straight distance ×
    /// <see cref="UnmeasuredRouteFactor"/>. A structure without its own threshold or capacity uses the defaults here.
    /// </summary>
    public class ProductionTransportSettingsDto
    {
        public const decimal MinSpeed = 0.1m, MaxSpeed = 50m;
        public const int MaxUnits = 1_000_000;
        public const decimal MinRouteFactor = 1m, MaxRouteFactor = 10m;
        public const int MaxMinutes = 10_080;

        /// <summary>4.317 = a vanilla player's walking speed.</summary>
        [JsonPropertyName("walkingSpeedBlocksPerSecond")]
        public decimal WalkingSpeedBlocksPerSecond { get; set; } = 4.317m;

        /// <summary>Raw units a worker carries when the structure has no <c>TransportCapacity</c> (from its level).</summary>
        [JsonPropertyName("defaultCarrierCapacity")]
        public int DefaultCarrierCapacity { get; set; } = 64;

        /// <summary>Output occupancy that triggers a transport, in % of the Output storage's cap, when the structure has
        /// no <c>TransportThreshold</c>.</summary>
        [JsonPropertyName("defaultThresholdPercent")]
        public int DefaultThresholdPercent { get; set; } = 75;

        /// <summary>The trigger in raw units for an uncapped Output storage without a structure threshold.</summary>
        [JsonPropertyName("defaultThresholdUnits")]
        public int DefaultThresholdUnits { get; set; } = 64;

        [JsonPropertyName("unmeasuredRouteFactor")]
        public decimal UnmeasuredRouteFactor { get; set; } = 1.4m;

        /// <summary>Blocks used for a leg when neither a lane nor both structures' positions are known.</summary>
        [JsonPropertyName("fallbackDistanceBlocks")]
        public int FallbackDistanceBlocks { get; set; } = 200;

        /// <summary>How long a carrier waits at a full stop before it re-routes (or returns).</summary>
        [JsonPropertyName("awaitingSpaceMinutes")]
        public int AwaitingSpaceMinutes { get; set; } = 30;

        /// <summary>Re-routes per order before the cargo goes back.</summary>
        [JsonPropertyName("maxReroutes")]
        public int MaxReroutes { get; set; } = 2;

        /// <summary>Lanes older than this are listed again for the game server to measure.</summary>
        [JsonPropertyName("laneMaxAgeHours")]
        public int LaneMaxAgeHours { get; set; } = 24;
    }

    /// <summary>
    /// Production engine timing (P3, PRODUCTION_KINDS § 4.3-4.4). A line settles every base cycle ÷ the level's cycle
    /// speed, on a grid anchored at the line's creation (no drift); a settle after a long pause accrues at most the
    /// catch-up cap. The weather is rolled once per in-game day of <see cref="WeatherDayMinutes"/> real minutes.
    /// </summary>
    public class ProductionEngineSettingsDto
    {
        public const int MinCycleMinutes = 1, MaxCycleMinutes = 1440;
        public const int MinCatchUpHours = 1, MaxCatchUpHours = 720;
        public const int MinWeatherDayMinutes = 1, MaxWeatherDayMinutes = 10080;

        [JsonPropertyName("baseCycleMinutes")]
        public int BaseCycleMinutes { get; set; } = 10;

        [JsonPropertyName("catchUpCapHours")]
        public int CatchUpCapHours { get; set; } = 24;

        /// <summary>20 = a vanilla Minecraft day.</summary>
        [JsonPropertyName("weatherDayMinutes")]
        public int WeatherDayMinutes { get; set; } = 20;
    }

    /// <summary>Range = (base range + power × factor) × capital multiplier for a capital district (G11).</summary>
    public class TerritorySettingsDto
    {
        public const int MaxBaseRange = 10_000;
        public const double MaxPowerFactor = 1_000;
        public const double MaxCapitalMultiplier = 10;

        [JsonPropertyName("baseRange")]
        public int BaseRange { get; set; } = 100;

        [JsonPropertyName("powerFactor")]
        public double PowerFactor { get; set; } = 2;

        [JsonPropertyName("capitalMultiplier")]
        public double CapitalMultiplier { get; set; } = 1.5;
    }

    /// <summary>A territory holder: <c>kind</c> Town or Kingdom (crown land is held by its kingdom) and its id.</summary>
    public class TerritoryHolderDto
    {
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = null!;

        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>Read only: filled in on read; null when the holder was deleted.</summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    // ----- Kingdom -----

    public class KingdomDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime? CreatedAt { get; set; }

        /// <summary>Read only.</summary>
        [JsonPropertyName("towns")]
        public List<TerritoryNavDto>? Towns { get; set; }

        /// <summary>Read only.</summary>
        [JsonPropertyName("provinces")]
        public List<TerritoryNavDto>? Provinces { get; set; }

        /// <summary>Read only.</summary>
        [JsonPropertyName("crownLands")]
        public List<TerritoryNavDto>? CrownLands { get; set; }
    }

    public class KingdomListDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("townCount")]
        public int TownCount { get; set; }

        [JsonPropertyName("crownLandCount")]
        public int CrownLandCount { get; set; }
    }

    /// <summary>Id and name of a related row (no back-references).</summary>
    public class TerritoryNavDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    // ----- Province -----

    public class ProvinceDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("kingdomId")]
        public int KingdomId { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime? CreatedAt { get; set; }

        /// <summary>Read only.</summary>
        [JsonPropertyName("kingdom")]
        public TerritoryNavDto? Kingdom { get; set; }

        /// <summary>Read only.</summary>
        [JsonPropertyName("towns")]
        public List<TerritoryNavDto>? Towns { get; set; }
    }

    public class ProvinceListDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("kingdomId")]
        public int KingdomId { get; set; }

        [JsonPropertyName("kingdomName")]
        public string? KingdomName { get; set; }

        [JsonPropertyName("townCount")]
        public int TownCount { get; set; }
    }

    // ----- Crown land -----

    public class CrownLandDto : IDomainNameDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("description")]
        public string Description { get; set; } = null!;

        [JsonPropertyName("createdAt")]
        public DateTime? CreatedAt { get; set; }

        [JsonPropertyName("allowEntry")]
        public bool? AllowEntry { get; set; }

        [JsonPropertyName("allowExit")]
        public bool? AllowExit { get; set; }

        /// <summary>The anchor region, drawn with the world task like a district's (G10).</summary>
        [JsonPropertyName("wgRegionId")]
        public string WgRegionId { get; set; } = null!;

        [JsonPropertyName("worldName")]
        public string? WorldName { get; set; }

        /// <summary>An existing Location as the arrival point (optional).</summary>
        [JsonPropertyName("locationId")]
        public int? LocationId { get; set; }

        [JsonPropertyName("kingdomId")]
        public int KingdomId { get; set; }

        /// <summary>Blocks from the nearest edge of the region (G10, G11).</summary>
        [JsonPropertyName("range")]
        public int Range { get; set; }

        /// <summary>Read only.</summary>
        [JsonPropertyName("kingdom")]
        public TerritoryNavDto? Kingdom { get; set; }

        /// <summary>Read only: the domain's sub-regions (edited through api/DomainSubRegions).</summary>
        [JsonPropertyName("subRegions")]
        public List<DomainSubRegionDto>? SubRegions { get; set; }
    }

    public class CrownLandListDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("wgRegionId")]
        public string WgRegionId { get; set; } = null!;

        [JsonPropertyName("worldName")]
        public string? WorldName { get; set; }

        [JsonPropertyName("kingdomId")]
        public int KingdomId { get; set; }

        [JsonPropertyName("kingdomName")]
        public string? KingdomName { get; set; }

        [JsonPropertyName("range")]
        public int Range { get; set; }
    }

    // ----- Conflicts -----

    public class TerritoryConflictDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("world")]
        public string World { get; set; } = null!;

        [JsonPropertyName("holderA")]
        public TerritoryHolderDto HolderA { get; set; } = null!;

        [JsonPropertyName("holderB")]
        public TerritoryHolderDto HolderB { get; set; } = null!;

        /// <summary>Open or Resolved.</summary>
        [JsonPropertyName("status")]
        public string Status { get; set; } = null!;

        [JsonPropertyName("stillOverlapping")]
        public bool StillOverlapping { get; set; }

        /// <summary>The contested chunks as <c>[x, z]</c> chunk coordinates.</summary>
        [JsonPropertyName("chunks")]
        public List<int[]> Chunks { get; set; } = new();

        [JsonPropertyName("chunkCount")]
        public int ChunkCount { get; set; }

        [JsonPropertyName("minChunkX")]
        public int MinChunkX { get; set; }

        [JsonPropertyName("minChunkZ")]
        public int MinChunkZ { get; set; }

        [JsonPropertyName("maxChunkX")]
        public int MaxChunkX { get; set; }

        [JsonPropertyName("maxChunkZ")]
        public int MaxChunkZ { get; set; }

        [JsonPropertyName("detectedAt")]
        public DateTime DetectedAt { get; set; }

        [JsonPropertyName("lastReportedAt")]
        public DateTime LastReportedAt { get; set; }

        /// <summary>None, Awarded or Redrawn.</summary>
        [JsonPropertyName("resolution")]
        public string Resolution { get; set; } = null!;

        [JsonPropertyName("awardedHolder")]
        public TerritoryHolderDto? AwardedHolder { get; set; }

        [JsonPropertyName("resolvedAt")]
        public DateTime? ResolvedAt { get; set; }

        [JsonPropertyName("resolvedByUserId")]
        public int? ResolvedByUserId { get; set; }

        [JsonPropertyName("resolutionNote")]
        public string? ResolutionNote { get; set; }

        [JsonPropertyName("flaggedStructures")]
        public List<TerritoryNavDto> FlaggedStructures { get; set; } = new();
    }

    public class TerritoryConflictListDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>"A vs B", for the dashboard's name column.</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("world")]
        public string World { get; set; } = null!;

        [JsonPropertyName("status")]
        public string Status { get; set; } = null!;

        [JsonPropertyName("stillOverlapping")]
        public bool StillOverlapping { get; set; }

        [JsonPropertyName("chunkCount")]
        public int ChunkCount { get; set; }

        [JsonPropertyName("resolution")]
        public string Resolution { get; set; } = null!;

        [JsonPropertyName("detectedAt")]
        public DateTime DetectedAt { get; set; }

        [JsonPropertyName("flaggedStructureCount")]
        public int FlaggedStructureCount { get; set; }
    }

    /// <summary>An admin's resolution (territory DESIGN § 3): award the area to one of the two holders, or mark the
    /// lines redrawn after changing power, regions or ranges.</summary>
    public class TerritoryConflictResolveDto
    {
        /// <summary>"Awarded" or "Redrawn".</summary>
        [JsonPropertyName("resolution")]
        public string Resolution { get; set; } = null!;

        /// <summary>Required for Awarded: one of the conflict's two holders.</summary>
        [JsonPropertyName("awardedHolder")]
        public TerritoryHolderDto? AwardedHolder { get; set; }

        [JsonPropertyName("note")]
        public string? Note { get; set; }
    }

    // ----- Structure loyalty and flag -----

    public class StructureTerritoryDto
    {
        [JsonPropertyName("structureId")]
        public int StructureId { get; set; }

        [JsonPropertyName("structureName")]
        public string? StructureName { get; set; }

        /// <summary>The structure's own town (through its district).</summary>
        [JsonPropertyName("townId")]
        public int? TownId { get; set; }

        /// <summary>Null = loyal to its own town.</summary>
        [JsonPropertyName("loyaltyTownId")]
        public int? LoyaltyTownId { get; set; }

        [JsonPropertyName("loyaltyTownName")]
        public string? LoyaltyTownName { get; set; }

        /// <summary>None, OutsideTerritory or Contested.</summary>
        [JsonPropertyName("territoryFlag")]
        public string TerritoryFlag { get; set; } = "None";

        [JsonPropertyName("territoryFlaggedAt")]
        public DateTime? TerritoryFlaggedAt { get; set; }

        [JsonPropertyName("territoryConflictId")]
        public int? TerritoryConflictId { get; set; }
    }

    public class StructureLoyaltyUpdateDto
    {
        /// <summary>Null = loyal to its own town.</summary>
        [JsonPropertyName("loyaltyTownId")]
        public int? LoyaltyTownId { get; set; }
    }

    // ----- Game server contract: inputs and report -----

    /// <summary>
    /// Everything the game server's chunk cache needs, in one read (<c>GET api/Territory/inputs</c>). The server
    /// rebuilds from it at start and after a <c>TerritoryChanged</c> realtime event. Ranges are already computed.
    /// </summary>
    public class TerritoryInputsDto
    {
        [JsonPropertyName("productionEnabled")]
        public bool ProductionEnabled { get; set; }

        [JsonPropertyName("settings")]
        public TerritorySettingsDto Settings { get; set; } = new();

        /// <summary>A hash of the payload: equal versions mean equal inputs, so a rebuild can be skipped.</summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("generatedAt")]
        public DateTime GeneratedAt { get; set; }

        [JsonPropertyName("kingdoms")]
        public List<TerritoryInputKingdomDto> Kingdoms { get; set; } = new();

        [JsonPropertyName("towns")]
        public List<TerritoryInputTownDto> Towns { get; set; } = new();

        [JsonPropertyName("districts")]
        public List<TerritoryInputRegionDto> Districts { get; set; } = new();

        [JsonPropertyName("crownLands")]
        public List<TerritoryInputRegionDto> CrownLands { get; set; } = new();

        /// <summary>Areas admins awarded to one holder: those chunks belong to it, whatever the overlap.</summary>
        [JsonPropertyName("awards")]
        public List<TerritoryInputAwardDto> Awards { get; set; } = new();
    }

    public class TerritoryInputKingdomDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;
    }

    public class TerritoryInputTownDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("kingdomId")]
        public int? KingdomId { get; set; }

        [JsonPropertyName("provinceId")]
        public int? ProvinceId { get; set; }

        [JsonPropertyName("power")]
        public int Power { get; set; }

        /// <summary>The range of the town's normal districts: base range + power × factor.</summary>
        [JsonPropertyName("range")]
        public double Range { get; set; }
    }

    /// <summary>A district or a crown land: a region (plus sub-regions) the territory is measured from.</summary>
    public class TerritoryInputRegionDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("world")]
        public string? World { get; set; }

        [JsonPropertyName("wgRegionId")]
        public string WgRegionId { get; set; } = null!;

        [JsonPropertyName("subRegionIds")]
        public List<string> SubRegionIds { get; set; } = new();

        /// <summary>Who holds the territory measured from this region: the district's town, the crown land's kingdom.</summary>
        [JsonPropertyName("holder")]
        public TerritoryHolderDto Holder { get; set; } = null!;

        /// <summary>Districts only.</summary>
        [JsonPropertyName("isCapital")]
        public bool IsCapital { get; set; }

        /// <summary>Holder rank: crown land 2 outranks towns 1 (territory DESIGN § 2).</summary>
        [JsonPropertyName("rank")]
        public int Rank { get; set; }

        /// <summary>Blocks from the nearest region edge, already computed (G11).</summary>
        [JsonPropertyName("range")]
        public double Range { get; set; }
    }

    public class TerritoryInputAwardDto
    {
        [JsonPropertyName("conflictId")]
        public int ConflictId { get; set; }

        [JsonPropertyName("world")]
        public string World { get; set; } = null!;

        [JsonPropertyName("holder")]
        public TerritoryHolderDto Holder { get; set; } = null!;

        [JsonPropertyName("chunks")]
        public List<int[]> Chunks { get; set; } = new();
    }

    /// <summary>
    /// The game server's territory report for one world (<c>PUT api/Territory/report</c>), a full snapshot: the overlaps
    /// of equal rank it found and the structures whose land is contested or outside their town's territory. Conflicts
    /// and flags of that world not in the report are updated accordingly.
    /// </summary>
    public class TerritoryReportDto
    {
        [JsonPropertyName("world")]
        public string World { get; set; } = null!;

        /// <summary>The inputs version the cache was built from (informational, logged).</summary>
        [JsonPropertyName("inputsVersion")]
        public string? InputsVersion { get; set; }

        [JsonPropertyName("overlaps")]
        public List<TerritoryReportOverlapDto> Overlaps { get; set; } = new();

        [JsonPropertyName("structures")]
        public List<TerritoryReportStructureDto> Structures { get; set; } = new();
    }

    public class TerritoryReportOverlapDto
    {
        [JsonPropertyName("holderA")]
        public TerritoryHolderDto HolderA { get; set; } = null!;

        [JsonPropertyName("holderB")]
        public TerritoryHolderDto HolderB { get; set; } = null!;

        [JsonPropertyName("chunks")]
        public List<int[]> Chunks { get; set; } = new();
    }

    public class TerritoryReportStructureDto
    {
        [JsonPropertyName("structureId")]
        public int StructureId { get; set; }

        /// <summary>OutsideTerritory or Contested.</summary>
        [JsonPropertyName("flag")]
        public string Flag { get; set; } = null!;

        /// <summary>For Contested: the pair of the overlap the structure stands in.</summary>
        [JsonPropertyName("holderA")]
        public TerritoryHolderDto? HolderA { get; set; }

        [JsonPropertyName("holderB")]
        public TerritoryHolderDto? HolderB { get; set; }
    }

    public class TerritoryReportResultDto
    {
        [JsonPropertyName("world")]
        public string World { get; set; } = null!;

        [JsonPropertyName("conflictsCreated")]
        public int ConflictsCreated { get; set; }

        [JsonPropertyName("conflictsUpdated")]
        public int ConflictsUpdated { get; set; }

        /// <summary>Open conflicts the report no longer shows (marked not overlapping; still open).</summary>
        [JsonPropertyName("conflictsNoLongerOverlapping")]
        public int ConflictsNoLongerOverlapping { get; set; }

        [JsonPropertyName("structuresFlagged")]
        public int StructuresFlagged { get; set; }

        [JsonPropertyName("structuresCleared")]
        public int StructuresCleared { get; set; }

        /// <summary>Report entries refused (unknown holder, unequal rank, unknown structure), with the reason.</summary>
        [JsonPropertyName("skipped")]
        public List<string> Skipped { get; set; } = new();
    }

    /// <summary>The effective ranges of one town, for the web app's town page (smoke § 1 step 1).</summary>
    public class TownTerritoryDto
    {
        [JsonPropertyName("townId")]
        public int TownId { get; set; }

        [JsonPropertyName("power")]
        public int Power { get; set; }

        [JsonPropertyName("kingdomId")]
        public int? KingdomId { get; set; }

        [JsonPropertyName("provinceId")]
        public int? ProvinceId { get; set; }

        [JsonPropertyName("range")]
        public double Range { get; set; }

        [JsonPropertyName("capitalRange")]
        public double CapitalRange { get; set; }

        [JsonPropertyName("districts")]
        public List<TerritoryInputRegionDto> Districts { get; set; } = new();
    }
}
