using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Dtos
{
    // Siege Survival phase 1 DTOs (knk-workspace docs/specs/siege-survival/DESIGN.md §10 "Proposed configuration
    // and runtime boundaries"; PandiO/knk-workspace#41, link S4): the authoring DTOs per survival entity, the
    // frozen match configuration the plugin reads in one call, readiness, the editor's per-round preview, and the
    // result/checkpoint contracts of phases 2–5 (contracts only; nothing of those is persisted in phase 1).
    //
    // World-bound locations follow the Siege DTOs: send the id of an existing Location ("locationId") or an inline
    // location ("location") - one without an id is created, one with an id is updated.

    // ==== Scenario settings (1:1 with SiegeScenario) ====

    public class SiegeSurvivalSettingsReadDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("siegeScenarioName")] public string? SiegeScenarioName { get; set; }
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
        [JsonPropertyName("minPlayers")] public int MinPlayers { get; set; }
        [JsonPropertyName("humanTeamId")] public int? HumanTeamId { get; set; }
        [JsonPropertyName("npcTeamId")] public int? NpcTeamId { get; set; }
        [JsonPropertyName("equipmentMode")] public SurvivalEquipmentMode EquipmentMode { get; set; }
        [JsonPropertyName("selectedKits")] public List<SurvivalSelectedKitDto> SelectedKits { get; set; } = new();
        [JsonPropertyName("defaultKitId")] public int? DefaultKitId { get; set; }
        [JsonPropertyName("baselineLoadoutKitId")] public int? BaselineLoadoutKitId { get; set; }
        [JsonPropertyName("intermissionMillis")] public int IntermissionMillis { get; set; }
        [JsonPropertyName("endWhenAllObjectivesCaptured")] public bool EndWhenAllObjectivesCaptured { get; set; }
        [JsonPropertyName("favorNearPlayersPercent")] public int FavorNearPlayersPercent { get; set; }
        [JsonPropertyName("nearZoneCount")] public int NearZoneCount { get; set; }
        [JsonPropertyName("stuckAfterMillis")] public int StuckAfterMillis { get; set; }
        [JsonPropertyName("lastEnemiesCount")] public int LastEnemiesCount { get; set; }
        [JsonPropertyName("respawnLimit")] public int RespawnLimit { get; set; }
        [JsonPropertyName("roundBackstopMillis")] public int? RoundBackstopMillis { get; set; }
        [JsonPropertyName("meaningfulDistance")] public double MeaningfulDistance { get; set; }
        [JsonPropertyName("coinsPerKill")] public decimal? CoinsPerKill { get; set; }
        [JsonPropertyName("xpPerKill")] public decimal? XpPerKill { get; set; }
        [JsonPropertyName("matchCurrencyPerKill")] public int MatchCurrencyPerKill { get; set; }
        [JsonPropertyName("killBonusMultiplier")] public decimal KillBonusMultiplier { get; set; }
    }

    public class SurvivalSelectedKitDto
    {
        [JsonPropertyName("kitId")] public int KitId { get; set; }
        [JsonPropertyName("kitName")] public string? KitName { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
    }

    // PUT api/SiegeSurvival/scenarios/{id}/settings creates or replaces the row. SelectedKits is a replace-set
    // (null = unchanged, [] = none). Null coinsPerKill / xpPerKill = the scenario's capture reward ÷ 10 (S32).
    public class SiegeSurvivalSettingsUpsertDto
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
        [JsonPropertyName("minPlayers")] public int MinPlayers { get; set; } = 1;
        [JsonPropertyName("humanTeamId")] public int? HumanTeamId { get; set; }
        [JsonPropertyName("npcTeamId")] public int? NpcTeamId { get; set; }
        [JsonPropertyName("equipmentMode")] public SurvivalEquipmentMode EquipmentMode { get; set; } = SurvivalEquipmentMode.OwnInventory;
        [JsonPropertyName("selectedKits")] public List<SurvivalSelectedKitUpsertDto>? SelectedKits { get; set; }
        [JsonPropertyName("defaultKitId")] public int? DefaultKitId { get; set; }
        [JsonPropertyName("baselineLoadoutKitId")] public int? BaselineLoadoutKitId { get; set; }
        [JsonPropertyName("intermissionMillis")] public int IntermissionMillis { get; set; } = 10_000;
        [JsonPropertyName("endWhenAllObjectivesCaptured")] public bool EndWhenAllObjectivesCaptured { get; set; }
        [JsonPropertyName("favorNearPlayersPercent")] public int FavorNearPlayersPercent { get; set; }
        [JsonPropertyName("nearZoneCount")] public int NearZoneCount { get; set; } = 1;
        [JsonPropertyName("stuckAfterMillis")] public int StuckAfterMillis { get; set; } = 30_000;
        [JsonPropertyName("lastEnemiesCount")] public int LastEnemiesCount { get; set; } = 3;
        [JsonPropertyName("respawnLimit")] public int RespawnLimit { get; set; } = 2;
        [JsonPropertyName("roundBackstopMillis")] public int? RoundBackstopMillis { get; set; } = 300_000;
        [JsonPropertyName("meaningfulDistance")] public double MeaningfulDistance { get; set; } = 1.0;
        [JsonPropertyName("coinsPerKill")] public decimal? CoinsPerKill { get; set; }
        [JsonPropertyName("xpPerKill")] public decimal? XpPerKill { get; set; }
        [JsonPropertyName("matchCurrencyPerKill")] public int MatchCurrencyPerKill { get; set; } = 70;
        [JsonPropertyName("killBonusMultiplier")] public decimal KillBonusMultiplier { get; set; } = 1.1m;
    }

    public class SurvivalSelectedKitUpsertDto
    {
        [JsonPropertyName("kitId")] public int KitId { get; set; }
        [JsonPropertyName("sortOrder")] public int? SortOrder { get; set; }
    }

    // GET api/SiegeSurvival/scenarios: every scenario with survival settings.
    public class SurvivalScenarioSummaryDto
    {
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("siegeScenarioName")] public string? SiegeScenarioName { get; set; }
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
        [JsonPropertyName("equipmentMode")] public SurvivalEquipmentMode EquipmentMode { get; set; }
        [JsonPropertyName("difficultyProfileCount")] public int DifficultyProfileCount { get; set; }
    }

    // ==== Lobby settings (1:1 with SiegeLobby) ====

    public class SurvivalLobbySettingsReadDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("siegeLobbyId")] public int SiegeLobbyId { get; set; }
        [JsonPropertyName("siegeLobbyName")] public string? SiegeLobbyName { get; set; }
        [JsonPropertyName("allCapturedOverride")] public SurvivalAllCapturedOverride AllCapturedOverride { get; set; }
    }

    public class SurvivalLobbySettingsUpsertDto
    {
        [JsonPropertyName("allCapturedOverride")] public SurvivalAllCapturedOverride AllCapturedOverride { get; set; } = SurvivalAllCapturedOverride.Inherit;
    }

    // ==== Difficulty profiles ====

    public class SurvivalDifficultyProfileDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        [JsonPropertyName("isDefault")] public bool IsDefault { get; set; }
        [JsonPropertyName("countPreset")] public SurvivalScalingPreset CountPreset { get; set; }
        [JsonPropertyName("countBase")] public double CountBase { get; set; }
        [JsonPropertyName("countStep")] public double CountStep { get; set; }
        [JsonPropertyName("countMultiplier")] public double CountMultiplier { get; set; }
        [JsonPropertyName("countSwitchRound")] public int CountSwitchRound { get; set; }
        [JsonPropertyName("statPreset")] public SurvivalScalingPreset StatPreset { get; set; }
        [JsonPropertyName("statStep")] public double StatStep { get; set; }
        [JsonPropertyName("statMultiplier")] public double StatMultiplier { get; set; }
        [JsonPropertyName("statSwitchRound")] public int StatSwitchRound { get; set; }
        [JsonPropertyName("healthWeight")] public double HealthWeight { get; set; }
        [JsonPropertyName("walkingSpeedWeight")] public double WalkingSpeedWeight { get; set; }
        [JsonPropertyName("meleeDamageWeight")] public double MeleeDamageWeight { get; set; }
        [JsonPropertyName("rangedDamageWeight")] public double RangedDamageWeight { get; set; }
        [JsonPropertyName("attackSpeedWeight")] public double AttackSpeedWeight { get; set; }
        [JsonPropertyName("perPlayerFactor")] public double PerPlayerFactor { get; set; }
        [JsonPropertyName("perPlayerAppliesToStats")] public bool PerPlayerAppliesToStats { get; set; }
        [JsonPropertyName("maxAlive")] public int MaxAlive { get; set; }
        [JsonPropertyName("statCap")] public double StatCap { get; set; }
        [JsonPropertyName("countCap")] public int CountCap { get; set; }
        [JsonPropertyName("repairRatePerSecond")] public double RepairRatePerSecond { get; set; }
        [JsonPropertyName("gateDamageFactor")] public double GateDamageFactor { get; set; }
        [JsonPropertyName("captureContributionWeight")] public double CaptureContributionWeight { get; set; }
        [JsonPropertyName("spawnIntervalMillis")] public int SpawnIntervalMillis { get; set; }
        [JsonPropertyName("aggroRadius")] public double? AggroRadius { get; set; }
        [JsonPropertyName("leashDistance")] public double? LeashDistance { get; set; }
    }

    public class SurvivalDifficultyProfileUpsertDto
    {
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [Required][JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("sortOrder")] public int? SortOrder { get; set; }
        [JsonPropertyName("isDefault")] public bool IsDefault { get; set; }
        [JsonPropertyName("countPreset")] public SurvivalScalingPreset CountPreset { get; set; } = SurvivalScalingPreset.Linear;
        [JsonPropertyName("countBase")] public double CountBase { get; set; } = 6;
        [JsonPropertyName("countStep")] public double CountStep { get; set; } = 2;
        [JsonPropertyName("countMultiplier")] public double CountMultiplier { get; set; } = 1.1;
        [JsonPropertyName("countSwitchRound")] public int CountSwitchRound { get; set; } = 9;
        [JsonPropertyName("statPreset")] public SurvivalScalingPreset StatPreset { get; set; } = SurvivalScalingPreset.Linear;
        [JsonPropertyName("statStep")] public double StatStep { get; set; } = 0.1;
        [JsonPropertyName("statMultiplier")] public double StatMultiplier { get; set; } = 1.1;
        [JsonPropertyName("statSwitchRound")] public int StatSwitchRound { get; set; } = 9;
        [JsonPropertyName("healthWeight")] public double HealthWeight { get; set; } = 1;
        [JsonPropertyName("walkingSpeedWeight")] public double WalkingSpeedWeight { get; set; } = 0.3;
        [JsonPropertyName("meleeDamageWeight")] public double MeleeDamageWeight { get; set; } = 1;
        [JsonPropertyName("rangedDamageWeight")] public double RangedDamageWeight { get; set; } = 1;
        [JsonPropertyName("attackSpeedWeight")] public double AttackSpeedWeight { get; set; } = 0.5;
        [JsonPropertyName("perPlayerFactor")] public double PerPlayerFactor { get; set; } = 0.25;
        [JsonPropertyName("perPlayerAppliesToStats")] public bool PerPlayerAppliesToStats { get; set; }
        [JsonPropertyName("maxAlive")] public int MaxAlive { get; set; } = 40;
        [JsonPropertyName("statCap")] public double StatCap { get; set; } = 8;
        [JsonPropertyName("countCap")] public int CountCap { get; set; }
        [JsonPropertyName("repairRatePerSecond")] public double RepairRatePerSecond { get; set; } = 50;
        [JsonPropertyName("gateDamageFactor")] public double GateDamageFactor { get; set; } = 1;
        [JsonPropertyName("captureContributionWeight")] public double CaptureContributionWeight { get; set; } = 1;
        [JsonPropertyName("spawnIntervalMillis")] public int SpawnIntervalMillis { get; set; } = 500;
        [JsonPropertyName("aggroRadius")] public double? AggroRadius { get; set; }
        [JsonPropertyName("leashDistance")] public double? LeashDistance { get; set; }
    }

    // ==== Monster types (scenario mix and special-profile mix share the shape) ====

    public class SurvivalMonsterTypeDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("entityType")] public string EntityType { get; set; } = string.Empty;
        [JsonPropertyName("weight")] public double Weight { get; set; }
        [JsonPropertyName("unlockRound")] public int UnlockRound { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
    }

    public class SurvivalMonsterTypeUpsertDto
    {
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [Required][JsonPropertyName("entityType")] public string EntityType { get; set; } = string.Empty;
        [JsonPropertyName("weight")] public double Weight { get; set; } = 1;
        [JsonPropertyName("unlockRound")] public int UnlockRound { get; set; } = 1;
        [JsonPropertyName("sortOrder")] public int? SortOrder { get; set; }
    }

    public class SurvivalSpecialWaveMonsterTypeDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("survivalSpecialWaveProfileId")] public int SurvivalSpecialWaveProfileId { get; set; }
        [JsonPropertyName("entityType")] public string EntityType { get; set; } = string.Empty;
        [JsonPropertyName("weight")] public double Weight { get; set; }
        [JsonPropertyName("unlockRound")] public int UnlockRound { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
    }

    public class SurvivalSpecialWaveMonsterTypeUpsertDto
    {
        [JsonPropertyName("survivalSpecialWaveProfileId")] public int SurvivalSpecialWaveProfileId { get; set; }
        [Required][JsonPropertyName("entityType")] public string EntityType { get; set; } = string.Empty;
        [JsonPropertyName("weight")] public double Weight { get; set; } = 1;
        [JsonPropertyName("unlockRound")] public int UnlockRound { get; set; } = 1;
        [JsonPropertyName("sortOrder")] public int? SortOrder { get; set; }
    }

    // ==== Special waves ====

    public class SurvivalSpecialWaveProfileDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        [JsonPropertyName("countOverride")] public int? CountOverride { get; set; }
        [JsonPropertyName("countMultiplier")] public double CountMultiplier { get; set; }
        [JsonPropertyName("additive")] public bool Additive { get; set; }
        [JsonPropertyName("monsterTypes")] public List<SurvivalSpecialWaveMonsterTypeDto> MonsterTypes { get; set; } = new();
    }

    public class SurvivalSpecialWaveProfileUpsertDto
    {
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [Required][JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [Required][JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("sortOrder")] public int? SortOrder { get; set; }
        [JsonPropertyName("countOverride")] public int? CountOverride { get; set; }
        [JsonPropertyName("countMultiplier")] public double CountMultiplier { get; set; } = 1;
        [JsonPropertyName("additive")] public bool Additive { get; set; }
    }

    public class SurvivalSpecialWaveEntryDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("survivalSpecialWaveProfileId")] public int SurvivalSpecialWaveProfileId { get; set; }
        [JsonPropertyName("profileKey")] public string? ProfileKey { get; set; }
        [JsonPropertyName("kind")] public SurvivalSpecialWaveKind Kind { get; set; }
        [JsonPropertyName("interval")] public int? Interval { get; set; }
        [JsonPropertyName("startWave")] public int? StartWave { get; set; }
        [JsonPropertyName("priority")] public int Priority { get; set; }
        [JsonPropertyName("wave")] public int? Wave { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
    }

    public class SurvivalSpecialWaveEntryUpsertDto
    {
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("survivalSpecialWaveProfileId")] public int SurvivalSpecialWaveProfileId { get; set; }
        [JsonPropertyName("kind")] public SurvivalSpecialWaveKind Kind { get; set; } = SurvivalSpecialWaveKind.Recurring;
        [JsonPropertyName("interval")] public int? Interval { get; set; }
        // Null = the interval ("every 5th wave" = 5, 10, 15 …).
        [JsonPropertyName("startWave")] public int? StartWave { get; set; }
        [JsonPropertyName("priority")] public int Priority { get; set; } = 1;
        [JsonPropertyName("wave")] public int? Wave { get; set; }
        [JsonPropertyName("sortOrder")] public int? SortOrder { get; set; }
    }

    // ==== Spawnzones ====

    public class SurvivalSpawnZoneDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        [JsonPropertyName("weight")] public double Weight { get; set; }
        [JsonPropertyName("anchorLocationId")] public int AnchorLocationId { get; set; }
        [JsonPropertyName("anchorLocation")] public LocationDto? AnchorLocation { get; set; }
        [JsonPropertyName("areaRadius")] public double? AreaRadius { get; set; }
        [JsonPropertyName("prerequisitePolicy")] public SurvivalPrerequisitePolicy PrerequisitePolicy { get; set; }
        [JsonPropertyName("prerequisiteObjectiveIds")] public List<int> PrerequisiteObjectiveIds { get; set; } = new();
        [JsonPropertyName("minWave")] public int MinWave { get; set; }
        [JsonPropertyName("maxWave")] public int? MaxWave { get; set; }
        [JsonPropertyName("forNormalWaves")] public bool ForNormalWaves { get; set; }
        [JsonPropertyName("forSpecialWaves")] public bool ForSpecialWaves { get; set; }
        [JsonPropertyName("laneAutoReroute")] public bool LaneAutoReroute { get; set; }
        [JsonPropertyName("points")] public List<SurvivalSpawnZonePointDto> Points { get; set; } = new();
    }

    // PrerequisiteObjectiveIds is a replace-set (null = unchanged, [] = none → initially active).
    public class SurvivalSpawnZoneUpsertDto
    {
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [Required][JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("sortOrder")] public int? SortOrder { get; set; }
        [JsonPropertyName("weight")] public double Weight { get; set; } = 1;
        [JsonPropertyName("anchorLocationId")] public int? AnchorLocationId { get; set; }
        [JsonPropertyName("anchorLocation")] public LocationDto? AnchorLocation { get; set; }
        [JsonPropertyName("areaRadius")] public double? AreaRadius { get; set; }
        [JsonPropertyName("prerequisitePolicy")] public SurvivalPrerequisitePolicy PrerequisitePolicy { get; set; } = SurvivalPrerequisitePolicy.All;
        [JsonPropertyName("prerequisiteObjectiveIds")] public List<int>? PrerequisiteObjectiveIds { get; set; }
        [JsonPropertyName("minWave")] public int MinWave { get; set; } = 1;
        [JsonPropertyName("maxWave")] public int? MaxWave { get; set; }
        [JsonPropertyName("forNormalWaves")] public bool ForNormalWaves { get; set; } = true;
        [JsonPropertyName("forSpecialWaves")] public bool ForSpecialWaves { get; set; } = true;
        [JsonPropertyName("laneAutoReroute")] public bool LaneAutoReroute { get; set; }
    }

    public class SurvivalSpawnZonePointDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("survivalSpawnZoneId")] public int SurvivalSpawnZoneId { get; set; }
        [JsonPropertyName("kind")] public SurvivalSpawnZonePointKind Kind { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("locationId")] public int LocationId { get; set; }
        [JsonPropertyName("location")] public LocationDto? Location { get; set; }
    }

    public class SurvivalSpawnZonePointUpsertDto
    {
        [JsonPropertyName("survivalSpawnZoneId")] public int SurvivalSpawnZoneId { get; set; }
        [JsonPropertyName("kind")] public SurvivalSpawnZonePointKind Kind { get; set; } = SurvivalSpawnZonePointKind.Spawn;
        [JsonPropertyName("sortOrder")] public int? SortOrder { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("locationId")] public int? LocationId { get; set; }
        [JsonPropertyName("location")] public LocationDto? Location { get; set; }
    }

    // ==== Target overrides ====

    public class SurvivalTargetOverrideDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("siegeObjectiveId")] public int? SiegeObjectiveId { get; set; }
        [JsonPropertyName("siegeObjectiveName")] public string? SiegeObjectiveName { get; set; }
        [JsonPropertyName("gateStructureId")] public int? GateStructureId { get; set; }
        [JsonPropertyName("gateStructureName")] public string? GateStructureName { get; set; }
        [JsonPropertyName("mode")] public SurvivalTargetOverrideMode Mode { get; set; }
    }

    // Exactly one of siegeObjectiveId / gateStructureId.
    public class SurvivalTargetOverrideUpsertDto
    {
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("siegeObjectiveId")] public int? SiegeObjectiveId { get; set; }
        [JsonPropertyName("gateStructureId")] public int? GateStructureId { get; set; }
        [JsonPropertyName("mode")] public SurvivalTargetOverrideMode Mode { get; set; } = SurvivalTargetOverrideMode.Standard;
    }

    // ==== Purchase points ====

    public class SurvivalPurchasePointDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        [JsonPropertyName("locationId")] public int LocationId { get; set; }
        [JsonPropertyName("location")] public LocationDto? Location { get; set; }
        [JsonPropertyName("interactionRange")] public double InteractionRange { get; set; }
        [JsonPropertyName("offers")] public List<SurvivalPurchaseOfferDto> Offers { get; set; } = new();
    }

    public class SurvivalPurchasePointUpsertDto
    {
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [Required][JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("sortOrder")] public int? SortOrder { get; set; }
        [JsonPropertyName("locationId")] public int? LocationId { get; set; }
        [JsonPropertyName("location")] public LocationDto? Location { get; set; }
        [JsonPropertyName("interactionRange")] public double InteractionRange { get; set; } = 3;
    }

    public class SurvivalPurchaseOfferDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("survivalPurchasePointId")] public int SurvivalPurchasePointId { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("quantity")] public int Quantity { get; set; }
        [JsonPropertyName("price")] public int Price { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
    }

    public class SurvivalPurchaseOfferUpsertDto
    {
        [JsonPropertyName("survivalPurchasePointId")] public int SurvivalPurchasePointId { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("quantity")] public int Quantity { get; set; } = 1;
        [JsonPropertyName("price")] public int Price { get; set; } = 100;
        [JsonPropertyName("sortOrder")] public int? SortOrder { get; set; }
    }

    // ==== Mystery Box ====

    public class SurvivalMysteryBoxPoolDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("variant")] public SurvivalMysteryBoxVariant Variant { get; set; }
        [JsonPropertyName("totalChancePercent")] public decimal TotalChancePercent { get; set; }
        [JsonPropertyName("entries")] public List<SurvivalMysteryBoxEntryDto> Entries { get; set; } = new();
    }

    public class SurvivalMysteryBoxPoolUpsertDto
    {
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [Required][JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("variant")] public SurvivalMysteryBoxVariant Variant { get; set; } = SurvivalMysteryBoxVariant.Items;
    }

    public class SurvivalMysteryBoxEntryDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("survivalMysteryBoxPoolId")] public int SurvivalMysteryBoxPoolId { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int? ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("enchantmentDefinitionId")] public int? EnchantmentDefinitionId { get; set; }
        [JsonPropertyName("enchantmentKey")] public string? EnchantmentKey { get; set; }
        [JsonPropertyName("enchantmentLevel")] public int? EnchantmentLevel { get; set; }
        [JsonPropertyName("chancePercent")] public decimal ChancePercent { get; set; }
        [JsonPropertyName("chanceBasisPoints")] public int ChanceBasisPoints { get; set; }
        [JsonPropertyName("enchantments")] public List<SurvivalMysteryBoxEntryEnchantmentDto> Enchantments { get; set; } = new();
    }

    // Items pool: itemBlueprintId (+ optional enchantments, a replace-set; null = unchanged); enchanted-books pool:
    // enchantmentDefinitionId + enchantmentLevel. chancePercent at 0.01 % precision.
    public class SurvivalMysteryBoxEntryUpsertDto
    {
        [JsonPropertyName("survivalMysteryBoxPoolId")] public int SurvivalMysteryBoxPoolId { get; set; }
        [JsonPropertyName("sortOrder")] public int? SortOrder { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int? ItemBlueprintId { get; set; }
        [JsonPropertyName("enchantmentDefinitionId")] public int? EnchantmentDefinitionId { get; set; }
        [JsonPropertyName("enchantmentLevel")] public int? EnchantmentLevel { get; set; }
        [JsonPropertyName("chancePercent")] public decimal ChancePercent { get; set; } = 10;
        [JsonPropertyName("enchantments")] public List<SurvivalMysteryBoxEntryEnchantmentUpsertDto>? Enchantments { get; set; }
    }

    public class SurvivalMysteryBoxEntryEnchantmentDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("enchantmentDefinitionId")] public int EnchantmentDefinitionId { get; set; }
        [JsonPropertyName("enchantmentKey")] public string? EnchantmentKey { get; set; }
        [JsonPropertyName("level")] public int Level { get; set; }
        [JsonPropertyName("chancePercent")] public decimal ChancePercent { get; set; }
        [JsonPropertyName("chanceBasisPoints")] public int ChanceBasisPoints { get; set; }
    }

    public class SurvivalMysteryBoxEntryEnchantmentUpsertDto
    {
        [JsonPropertyName("enchantmentDefinitionId")] public int EnchantmentDefinitionId { get; set; }
        [JsonPropertyName("level")] public int Level { get; set; } = 1;
        [JsonPropertyName("chancePercent")] public decimal ChancePercent { get; set; } = 25;
    }

    public class SurvivalMysteryBoxPointDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        [JsonPropertyName("locationId")] public int LocationId { get; set; }
        [JsonPropertyName("location")] public LocationDto? Location { get; set; }
        [JsonPropertyName("interactionRange")] public double InteractionRange { get; set; }
        [JsonPropertyName("survivalMysteryBoxPoolId")] public int SurvivalMysteryBoxPoolId { get; set; }
        [JsonPropertyName("poolName")] public string? PoolName { get; set; }
        [JsonPropertyName("blockType")] public string BlockType { get; set; } = string.Empty;
        [JsonPropertyName("rollPrice")] public int RollPrice { get; set; }
        [JsonPropertyName("claimTimeoutMillis")] public int ClaimTimeoutMillis { get; set; }
    }

    public class SurvivalMysteryBoxPointUpsertDto
    {
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [Required][JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("sortOrder")] public int? SortOrder { get; set; }
        [JsonPropertyName("locationId")] public int? LocationId { get; set; }
        [JsonPropertyName("location")] public LocationDto? Location { get; set; }
        [JsonPropertyName("interactionRange")] public double InteractionRange { get; set; } = 3;
        [JsonPropertyName("survivalMysteryBoxPoolId")] public int SurvivalMysteryBoxPoolId { get; set; }
        [JsonPropertyName("blockType")] public string BlockType { get; set; } = "minecraft:end_portal_frame";
        [JsonPropertyName("rollPrice")] public int RollPrice { get; set; } = 1000;
        [JsonPropertyName("claimTimeoutMillis")] public int ClaimTimeoutMillis { get; set; } = 10_000;
    }

    // ==== Readiness (DESIGN §10 "Readiness validates…") ====
    // Codes are the stable strings of knk-core's survival readiness (end.*, scaling.*, mix.*, special.*, spawn.*,
    // box.*) plus the API-only ones (team.*, kits.*, profile.*, shop.*, settings.*) and the ordinary Siege codes of
    // SiegeScenarioReadiness, so one list says whether a match may start.

    public class SurvivalReadinessDto
    {
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("siegeLobbyId")] public int? SiegeLobbyId { get; set; }
        [JsonPropertyName("difficultyProfileId")] public int? DifficultyProfileId { get; set; }
        [JsonPropertyName("isReady")] public bool IsReady { get; set; }
        [JsonPropertyName("errors")] public List<SiegeReadinessIssueDto> Errors { get; set; } = new();
        [JsonPropertyName("warnings")] public List<SiegeReadinessIssueDto> Warnings { get; set; } = new();
    }

    // ==== Frozen match configuration (GET api/SiegeSurvival/scenarios/{id}/configuration) ====
    // Everything the plugin needs for one match in one payload, every default resolved (teams, kill rewards from
    // the capture rewards, the lobby's all-captured override against the scenario). Snapshot at match start
    // (DESIGN §7 "Snapshot config at match start", §10 "Changes made in the editor apply to future matches").

    public class SurvivalConfigurationDto
    {
        [JsonPropertyName("generatedAt")] public DateTime GeneratedAt { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("siegeScenarioName")] public string SiegeScenarioName { get; set; } = string.Empty;
        [JsonPropertyName("siegeLobbyId")] public int? SiegeLobbyId { get; set; }
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
        [JsonPropertyName("minPlayers")] public int MinPlayers { get; set; }
        [JsonPropertyName("humanTeamId")] public int? HumanTeamId { get; set; }
        [JsonPropertyName("npcTeamId")] public int? NpcTeamId { get; set; }
        [JsonPropertyName("allowRecapture")] public bool AllowRecapture { get; set; }
        [JsonPropertyName("intermissionMillis")] public int IntermissionMillis { get; set; }
        [JsonPropertyName("equipment")] public SurvivalEquipmentConfigDto Equipment { get; set; } = new();
        [JsonPropertyName("endConditions")] public SurvivalEndConditionsConfigDto EndConditions { get; set; } = new();
        [JsonPropertyName("objectives")] public List<SurvivalObjectiveConfigDto> Objectives { get; set; } = new();
        [JsonPropertyName("gates")] public List<SurvivalGateConfigDto> Gates { get; set; } = new();
        [JsonPropertyName("stuck")] public SurvivalStuckConfigDto Stuck { get; set; } = new();
        [JsonPropertyName("spawn")] public SurvivalSpawnConfigDto Spawn { get; set; } = new();
        [JsonPropertyName("difficulty")] public SurvivalDifficultyConfigDto? Difficulty { get; set; }
        [JsonPropertyName("difficultyProfiles")] public List<SurvivalDifficultyConfigDto> DifficultyProfiles { get; set; } = new();
        [JsonPropertyName("monsterTypes")] public List<SurvivalMonsterTypeConfigDto> MonsterTypes { get; set; } = new();
        [JsonPropertyName("specialWaves")] public SurvivalSpecialWavesConfigDto SpecialWaves { get; set; } = new();
        [JsonPropertyName("killRewards")] public SurvivalKillRewardsConfigDto KillRewards { get; set; } = new();
        [JsonPropertyName("purchasePoints")] public List<SurvivalPurchasePointDto> PurchasePoints { get; set; } = new();
        [JsonPropertyName("mysteryBoxPoints")] public List<SurvivalMysteryBoxPointConfigDto> MysteryBoxPoints { get; set; } = new();
        [JsonPropertyName("readiness")] public SurvivalReadinessDto Readiness { get; set; } = new();
    }

    public class SurvivalEquipmentConfigDto
    {
        [JsonPropertyName("mode")] public SurvivalEquipmentMode Mode { get; set; }
        [JsonPropertyName("selectedKits")] public List<SurvivalSelectedKitDto> SelectedKits { get; set; } = new();
        [JsonPropertyName("defaultKitId")] public int? DefaultKitId { get; set; }
        [JsonPropertyName("baselineLoadoutKitId")] public int? BaselineLoadoutKitId { get; set; }
    }

    public class SurvivalEndConditionsConfigDto
    {
        [JsonPropertyName("scenarioAllCaptured")] public bool ScenarioAllCaptured { get; set; }
        [JsonPropertyName("lobbyOverride")] public SurvivalAllCapturedOverride LobbyOverride { get; set; }
        // The resolved value: the lobby's override when not Inherit, else the scenario's.
        [JsonPropertyName("allCapturedEnabled")] public bool AllCapturedEnabled { get; set; }
        [JsonPropertyName("instaLoseObjectiveIds")] public List<int> InstaLoseObjectiveIds { get; set; } = new();
    }

    public class SurvivalObjectiveConfigDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("instaLose")] public bool InstaLose { get; set; }
        [JsonPropertyName("gateStructureId")] public int? GateStructureId { get; set; }
        [JsonPropertyName("captureLocation")] public LocationDto? CaptureLocation { get; set; }
        [JsonPropertyName("targetOverride")] public SurvivalTargetOverrideMode TargetOverride { get; set; }
    }

    public class SurvivalGateConfigDto
    {
        [JsonPropertyName("gateStructureId")] public int GateStructureId { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("isObjectiveGate")] public bool IsObjectiveGate { get; set; }
        [JsonPropertyName("targetOverride")] public SurvivalTargetOverrideMode TargetOverride { get; set; }
    }

    public class SurvivalStuckConfigDto
    {
        [JsonPropertyName("stuckAfterMillis")] public int StuckAfterMillis { get; set; }
        [JsonPropertyName("lastEnemiesCount")] public int LastEnemiesCount { get; set; }
        [JsonPropertyName("respawnLimit")] public int RespawnLimit { get; set; }
        [JsonPropertyName("roundBackstopMillis")] public int? RoundBackstopMillis { get; set; }
        [JsonPropertyName("meaningfulDistance")] public double MeaningfulDistance { get; set; }
    }

    public class SurvivalSpawnConfigDto
    {
        [JsonPropertyName("favorNearPlayersPercent")] public int FavorNearPlayersPercent { get; set; }
        [JsonPropertyName("nearZoneCount")] public int NearZoneCount { get; set; }
        [JsonPropertyName("zones")] public List<SurvivalSpawnZoneDto> Zones { get; set; } = new();
    }

    public class SurvivalScalingCurveDto
    {
        [JsonPropertyName("preset")] public SurvivalScalingPreset Preset { get; set; }
        [JsonPropertyName("base")] public double Base { get; set; }
        [JsonPropertyName("step")] public double Step { get; set; }
        [JsonPropertyName("multiplier")] public double Multiplier { get; set; }
        [JsonPropertyName("switchRound")] public int SwitchRound { get; set; }
    }

    public class SurvivalStatWeightsDto
    {
        [JsonPropertyName("health")] public double Health { get; set; }
        [JsonPropertyName("walkingSpeed")] public double WalkingSpeed { get; set; }
        [JsonPropertyName("meleeDamage")] public double MeleeDamage { get; set; }
        [JsonPropertyName("rangedDamage")] public double RangedDamage { get; set; }
        [JsonPropertyName("attackSpeed")] public double AttackSpeed { get; set; }
    }

    public class SurvivalPerPlayerScalingDto
    {
        [JsonPropertyName("factor")] public double Factor { get; set; }
        [JsonPropertyName("applyToStats")] public bool ApplyToStats { get; set; }
    }

    public class SurvivalDifficultyConfigDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("isDefault")] public bool IsDefault { get; set; }
        [JsonPropertyName("countCurve")] public SurvivalScalingCurveDto CountCurve { get; set; } = new();
        [JsonPropertyName("statCurve")] public SurvivalScalingCurveDto StatCurve { get; set; } = new();
        [JsonPropertyName("statWeights")] public SurvivalStatWeightsDto StatWeights { get; set; } = new();
        [JsonPropertyName("perPlayer")] public SurvivalPerPlayerScalingDto PerPlayer { get; set; } = new();
        [JsonPropertyName("maxAlive")] public int MaxAlive { get; set; }
        [JsonPropertyName("statCap")] public double StatCap { get; set; }
        [JsonPropertyName("countCap")] public int CountCap { get; set; }
        [JsonPropertyName("repairRatePerSecond")] public double RepairRatePerSecond { get; set; }
        [JsonPropertyName("gateDamageFactor")] public double GateDamageFactor { get; set; }
        [JsonPropertyName("captureContributionWeight")] public double CaptureContributionWeight { get; set; }
        [JsonPropertyName("spawnIntervalMillis")] public int SpawnIntervalMillis { get; set; }
        [JsonPropertyName("aggroRadius")] public double? AggroRadius { get; set; }
        [JsonPropertyName("leashDistance")] public double? LeashDistance { get; set; }
    }

    public class SurvivalMonsterTypeConfigDto
    {
        [JsonPropertyName("entityType")] public string EntityType { get; set; } = string.Empty;
        [JsonPropertyName("weight")] public double Weight { get; set; }
        [JsonPropertyName("unlockRound")] public int UnlockRound { get; set; }
    }

    public class SurvivalSpecialWavesConfigDto
    {
        [JsonPropertyName("profiles")] public List<SurvivalSpecialWaveProfileConfigDto> Profiles { get; set; } = new();
        [JsonPropertyName("entries")] public List<SurvivalSpecialWaveEntryDto> Entries { get; set; } = new();
    }

    public class SurvivalSpecialWaveProfileConfigDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("countOverride")] public int? CountOverride { get; set; }
        [JsonPropertyName("countMultiplier")] public double CountMultiplier { get; set; }
        [JsonPropertyName("additive")] public bool Additive { get; set; }
        [JsonPropertyName("monsterTypes")] public List<SurvivalMonsterTypeConfigDto> MonsterTypes { get; set; } = new();
    }

    public class SurvivalKillRewardsConfigDto
    {
        [JsonPropertyName("coinsPerKill")] public decimal CoinsPerKill { get; set; }
        [JsonPropertyName("xpPerKill")] public decimal XpPerKill { get; set; }
        [JsonPropertyName("matchCurrencyPerKill")] public int MatchCurrencyPerKill { get; set; }
        [JsonPropertyName("bonusMultiplier")] public decimal BonusMultiplier { get; set; }
    }

    public class SurvivalMysteryBoxPointConfigDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("location")] public LocationDto? Location { get; set; }
        [JsonPropertyName("interactionRange")] public double InteractionRange { get; set; }
        [JsonPropertyName("blockType")] public string BlockType { get; set; } = string.Empty;
        [JsonPropertyName("rollPrice")] public int RollPrice { get; set; }
        [JsonPropertyName("claimTimeoutMillis")] public int ClaimTimeoutMillis { get; set; }
        [JsonPropertyName("pool")] public SurvivalMysteryBoxPoolDto Pool { get; set; } = new();
    }

    // ==== Editor preview (DESIGN §11 "preview the resulting count and stat multiplier per round") ====

    public class SurvivalRoundPreviewDto
    {
        [JsonPropertyName("round")] public int Round { get; set; }
        [JsonPropertyName("enemyCount")] public int EnemyCount { get; set; }
        [JsonPropertyName("curveValue")] public double CurveValue { get; set; }
        [JsonPropertyName("health")] public double Health { get; set; }
        [JsonPropertyName("walkingSpeed")] public double WalkingSpeed { get; set; }
        [JsonPropertyName("meleeDamage")] public double MeleeDamage { get; set; }
        [JsonPropertyName("rangedDamage")] public double RangedDamage { get; set; }
        [JsonPropertyName("attackSpeed")] public double AttackSpeed { get; set; }
    }

    public class SurvivalPreviewDto
    {
        [JsonPropertyName("difficultyProfileId")] public int DifficultyProfileId { get; set; }
        [JsonPropertyName("players")] public int Players { get; set; }
        [JsonPropertyName("rounds")] public List<SurvivalRoundPreviewDto> Rounds { get; set; } = new();
    }

    // ==== Result and checkpoint contracts (DESIGN §3, §9, §10 "Runtime/checkpoint") ====
    // Phase 1 (link S4) defined the shapes the plugin reports with; phase 5 (link S8a) accepts them:
    // POST api/siege-survival/matches/{id}/result stores the result once (same body again = 200 the stored row,
    // different body = 409 ResultAlreadyStored) and settles the per-kill rewards through the ledger when the end
    // reason settles (S62); PUT .../checkpoint upserts the latest checkpoint. Fields marked "API-filled" are set by
    // the API in its answer and ignored in the request body (they take no part in the idempotency comparison).

    public class SurvivalMatchResultDto
    {
        [JsonPropertyName("siegeMatchId")] public int? SiegeMatchId { get; set; }
        [JsonPropertyName("siegeLobbyId")] public int SiegeLobbyId { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("difficultyProfileId")] public int? DifficultyProfileId { get; set; }
        [JsonPropertyName("configurationGeneratedAt")] public DateTime? ConfigurationGeneratedAt { get; set; }
        [JsonPropertyName("startedAt")] public DateTime? StartedAt { get; set; }
        [JsonPropertyName("endedAt")] public DateTime? EndedAt { get; set; }
        [JsonPropertyName("endReason")] public SurvivalEndReason EndReason { get; set; }
        // A wipe in wave 10 = reached 10, completed 9 (DESIGN §3).
        [JsonPropertyName("reachedWave")] public int ReachedWave { get; set; }
        [JsonPropertyName("completedWave")] public int CompletedWave { get; set; }
        [JsonPropertyName("earnedKills")] public int EarnedKills { get; set; }
        [JsonPropertyName("systemKills")] public int SystemKills { get; set; }
        [JsonPropertyName("players")] public List<SurvivalPlayerResultDto> Players { get; set; } = new();
        [JsonPropertyName("settlement")] public SurvivalSettlementDto Settlement { get; set; } = new();
        // Phase 5 (link S8a). The equipment mode the match ran with; null in the request = the scenario's setting.
        [JsonPropertyName("equipmentMode")] public SurvivalEquipmentMode? EquipmentMode { get; set; }
        // API-filled: the reward policy the settlement used (DESIGN §9 "store ... the reward-policy version").
        [JsonPropertyName("rewardPolicyVersion")] public string? RewardPolicyVersion { get; set; }
        // API-filled: Plugin (posted by the game server) or Abort (written by the API at a Siege abort, nothing settled).
        [JsonPropertyName("source")] public SurvivalMatchResultSource? Source { get; set; }
        // API-filled: when the row was stored.
        [JsonPropertyName("recordedAt")] public DateTime? RecordedAt { get; set; }
    }

    public class SurvivalPlayerResultDto
    {
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("presentAtEnd")] public bool PresentAtEnd { get; set; }
        [JsonPropertyName("leftAt")] public DateTime? LeftAt { get; set; }
        [JsonPropertyName("kitId")] public int? KitId { get; set; }
        [JsonPropertyName("kills")] public int Kills { get; set; }
        [JsonPropertyName("bonusKills")] public int BonusKills { get; set; }
        [JsonPropertyName("deaths")] public int Deaths { get; set; }
        [JsonPropertyName("repairWork")] public double RepairWork { get; set; }
        [JsonPropertyName("matchCurrencyEarned")] public long MatchCurrencyEarned { get; set; }
        [JsonPropertyName("matchCurrencySpent")] public long MatchCurrencySpent { get; set; }
        [JsonPropertyName("finalMatchBalance")] public long FinalMatchBalance { get; set; }
        // Exact accruals (tenths), rounded down once at settlement (DESIGN §9).
        [JsonPropertyName("coinsAccrued")] public decimal CoinsAccrued { get; set; }
        [JsonPropertyName("xpAccrued")] public decimal XpAccrued { get; set; }
        [JsonPropertyName("coinsSettled")] public int CoinsSettled { get; set; }
        [JsonPropertyName("xpSettled")] public int XpSettled { get; set; }
        // Phase 5 (link S8a), API-filled from the match's Mystery Box roll rows (S7a): rolls made, claimed, forfeited.
        [JsonPropertyName("mysteryBoxRolls")] public int MysteryBoxRolls { get; set; }
        [JsonPropertyName("mysteryBoxClaims")] public int MysteryBoxClaims { get; set; }
        [JsonPropertyName("mysteryBoxForfeits")] public int MysteryBoxForfeits { get; set; }
    }

    public class SurvivalSettlementDto
    {
        // False for aborts (no players left, admin abort, restart, technical limit) and leavers (S62).
        [JsonPropertyName("settlesRewards")] public bool SettlesRewards { get; set; }
        [JsonPropertyName("totalCoins")] public int TotalCoins { get; set; }
        [JsonPropertyName("totalXp")] public int TotalXp { get; set; }
        // The ledger idempotency key the settlement uses: siege-survival:{matchId} (link S8a; the PvP posting keeps
        // siege-match:{id}). Optional in the request; when given it must be this key.
        [JsonPropertyName("ledgerKey")] public string? LedgerKey { get; set; }
        // Phase 5 (link S8a), API-filled: when the posting was made (also set when there was nothing to pay), its
        // public id (null when no leg was > 0 or nothing settles) and how many players got a leg.
        [JsonPropertyName("settledAt")] public DateTime? SettledAt { get; set; }
        [JsonPropertyName("ledgerPostingId")] public string? LedgerPostingId { get; set; }
        [JsonPropertyName("playersSettled")] public int PlayersSettled { get; set; }
    }

    public class SurvivalMatchCheckpointDto
    {
        [JsonPropertyName("siegeMatchId")] public int? SiegeMatchId { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("difficultyProfileId")] public int? DifficultyProfileId { get; set; }
        [JsonPropertyName("capturedAt")] public DateTime CapturedAt { get; set; }
        [JsonPropertyName("substate")] public SurvivalSubstate Substate { get; set; }
        [JsonPropertyName("wave")] public int Wave { get; set; }
        [JsonPropertyName("completedWave")] public int CompletedWave { get; set; }
        [JsonPropertyName("specialProfileKey")] public string? SpecialProfileKey { get; set; }
        [JsonPropertyName("aliveEnemies")] public int AliveEnemies { get; set; }
        [JsonPropertyName("queuedEnemies")] public int QueuedEnemies { get; set; }
        [JsonPropertyName("npcHeldObjectiveIds")] public List<int> NpcHeldObjectiveIds { get; set; } = new();
        [JsonPropertyName("activeSpawnZoneIds")] public List<int> ActiveSpawnZoneIds { get; set; } = new();
        [JsonPropertyName("doorRepairs")] public List<SurvivalDoorRepairCheckpointDto> DoorRepairs { get; set; } = new();
        [JsonPropertyName("players")] public List<SurvivalPlayerCheckpointDto> Players { get; set; } = new();
        // Phase 5 (link S8a), API-filled: when the stored checkpoint was last written and how many times (1 = first).
        [JsonPropertyName("updatedAt")] public DateTime? UpdatedAt { get; set; }
        [JsonPropertyName("revision")] public int? Revision { get; set; }
    }

    public class SurvivalDoorRepairCheckpointDto
    {
        [JsonPropertyName("gateDoorId")] public int GateDoorId { get; set; }
        [JsonPropertyName("destroyed")] public bool Destroyed { get; set; }
        [JsonPropertyName("repairWork")] public double RepairWork { get; set; }
        [JsonPropertyName("repairTarget")] public double RepairTarget { get; set; }
        [JsonPropertyName("repairers")] public int Repairers { get; set; }
    }

    public class SurvivalPlayerCheckpointDto
    {
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("ghost")] public bool Ghost { get; set; }
        [JsonPropertyName("kitId")] public int? KitId { get; set; }
        [JsonPropertyName("matchBalance")] public long MatchBalance { get; set; }
        [JsonPropertyName("kills")] public int Kills { get; set; }
        [JsonPropertyName("deaths")] public int Deaths { get; set; }
        [JsonPropertyName("pendingMysteryBoxRollId")] public string? PendingMysteryBoxRollId { get; set; }
    }
}
