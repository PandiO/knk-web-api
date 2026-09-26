using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Dtos
{
    // Siege Phase 2 DTOs (docs/specs/siege-minigame/DESIGN.md §3.3–3.9, §11.2): scenario + owned
    // children, lobby, configuration singleton, readiness and the plugin's runtime-config payload.
    // Match DTOs come with the match endpoints (Phase 6).
    //
    // World-bound location fields follow GateStructureDto: send either the id of an existing Location
    // ("hubLocationId") or an inline location ("hubLocation") - an inline one without an id is created,
    // one with an id is updated.

    // ---- SiegeScenario ----

    public class SiegeScenarioReadDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("townId")] public int TownId { get; set; }
        [JsonPropertyName("townName")] public string? TownName { get; set; }
        [JsonPropertyName("districts")] public List<SiegeScenarioDistrictDto> Districts { get; set; } = new();
        [JsonPropertyName("hubLocationId")] public int HubLocationId { get; set; }
        [JsonPropertyName("hubLocation")] public LocationDto? HubLocation { get; set; }
        [JsonPropertyName("playersMin")] public int PlayersMin { get; set; }
        [JsonPropertyName("playersMax")] public int PlayersMax { get; set; }
        [JsonPropertyName("minTitleBracketId")] public int? MinTitleBracketId { get; set; }
        [JsonPropertyName("matchDurationMinSeconds")] public int MatchDurationMinSeconds { get; set; }
        [JsonPropertyName("matchDurationPerPlayerSeconds")] public int MatchDurationPerPlayerSeconds { get; set; }
        [JsonPropertyName("matchDurationMaxSeconds")] public int MatchDurationMaxSeconds { get; set; }
        [JsonPropertyName("coinRewardWin")] public int CoinRewardWin { get; set; }
        [JsonPropertyName("expRewardWin")] public int ExpRewardWin { get; set; }
        [JsonPropertyName("gemRewardWin")] public int GemRewardWin { get; set; }
        [JsonPropertyName("coinRewardHolding")] public int CoinRewardHolding { get; set; }
        [JsonPropertyName("expRewardHolding")] public int ExpRewardHolding { get; set; }
        [JsonPropertyName("coinRewardCapture")] public int CoinRewardCapture { get; set; }
        [JsonPropertyName("expRewardCapture")] public int ExpRewardCapture { get; set; }
        [JsonPropertyName("lockdownScenarioArea")] public bool LockdownScenarioArea { get; set; }
        [JsonPropertyName("allowRecapture")] public bool AllowRecapture { get; set; }
        [JsonPropertyName("enchantDropsEnabled")] public bool EnchantDropsEnabled { get; set; }

        // Owned children, ordered by (SortOrder, Id).
        [JsonPropertyName("teams")] public List<SiegeTeamReadDto> Teams { get; set; } = new();
        [JsonPropertyName("objectives")] public List<SiegeObjectiveReadDto> Objectives { get; set; } = new();
        [JsonPropertyName("gates")] public List<SiegeScenarioGateDto> Gates { get; set; } = new();
    }

    // Teams and objectives are managed through their own endpoints (owned child collections) - any
    // "teams"/"objectives" property in the payload is ignored. Districts and gates are M2M joins
    // replaced as a set; null leaves the current set unchanged, [] clears it.
    public class SiegeScenarioUpsertDto
    {
        [Required][JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("townId")] public int TownId { get; set; }
        [JsonPropertyName("districts")] public List<SiegeScenarioDistrictUpsertDto>? Districts { get; set; }
        [JsonPropertyName("hubLocationId")] public int? HubLocationId { get; set; }
        [JsonPropertyName("hubLocation")] public LocationDto? HubLocation { get; set; }
        [JsonPropertyName("playersMin")] public int PlayersMin { get; set; } = 2;
        [JsonPropertyName("playersMax")] public int PlayersMax { get; set; } = 50;
        [JsonPropertyName("minTitleBracketId")] public int? MinTitleBracketId { get; set; }
        [JsonPropertyName("matchDurationMinSeconds")] public int MatchDurationMinSeconds { get; set; } = 300;
        [JsonPropertyName("matchDurationPerPlayerSeconds")] public int MatchDurationPerPlayerSeconds { get; set; } = 75;
        [JsonPropertyName("matchDurationMaxSeconds")] public int MatchDurationMaxSeconds { get; set; } = 1800;
        [JsonPropertyName("coinRewardWin")] public int CoinRewardWin { get; set; } = 100;
        [JsonPropertyName("expRewardWin")] public int ExpRewardWin { get; set; } = 10;
        [JsonPropertyName("gemRewardWin")] public int GemRewardWin { get; set; } = 1;
        [JsonPropertyName("coinRewardHolding")] public int CoinRewardHolding { get; set; } = 50;
        [JsonPropertyName("expRewardHolding")] public int ExpRewardHolding { get; set; } = 5;
        [JsonPropertyName("coinRewardCapture")] public int CoinRewardCapture { get; set; } = 50;
        [JsonPropertyName("expRewardCapture")] public int ExpRewardCapture { get; set; } = 5;
        [JsonPropertyName("lockdownScenarioArea")] public bool LockdownScenarioArea { get; set; } = true;
        [JsonPropertyName("allowRecapture")] public bool AllowRecapture { get; set; }
        [JsonPropertyName("enchantDropsEnabled")] public bool EnchantDropsEnabled { get; set; } = true;
        [JsonPropertyName("gates")] public List<SiegeScenarioGateUpsertDto>? Gates { get; set; }
    }

    public class SiegeScenarioListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("townId")] public int TownId { get; set; }
        [JsonPropertyName("townName")] public string? TownName { get; set; }
        [JsonPropertyName("teamCount")] public int TeamCount { get; set; }
        [JsonPropertyName("objectiveCount")] public int ObjectiveCount { get; set; }
        [JsonPropertyName("gateCount")] public int GateCount { get; set; }
    }

    public class SiegeScenarioDistrictDto
    {
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("districtId")] public int DistrictId { get; set; }
        [JsonPropertyName("districtName")] public string? DistrictName { get; set; }
    }

    public class SiegeScenarioDistrictUpsertDto
    {
        [JsonPropertyName("districtId")] public int DistrictId { get; set; }
    }

    public class SiegeScenarioGateDto
    {
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("gateStructureId")] public int GateStructureId { get; set; }
        [JsonPropertyName("gateStructureName")] public string? GateStructureName { get; set; }
        [JsonPropertyName("initialOwnerTeamId")] public int? InitialOwnerTeamId { get; set; }
        [JsonPropertyName("initialState")] public GateDoorOpenState InitialState { get; set; }
        [JsonPropertyName("damageable")] public bool Damageable { get; set; }
    }

    public class SiegeScenarioGateUpsertDto
    {
        [JsonPropertyName("gateStructureId")] public int GateStructureId { get; set; }
        // null (or 0) -> the scenario's first Defender team.
        [JsonPropertyName("initialOwnerTeamId")] public int? InitialOwnerTeamId { get; set; }
        [JsonPropertyName("initialState")] public GateDoorOpenState InitialState { get; set; } = GateDoorOpenState.CLOSED;
        [JsonPropertyName("damageable")] public bool Damageable { get; set; } = true;
    }

    // ---- SiegeTeam ----

    public class SiegeTeamReadDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        [JsonPropertyName("role")] public SiegeTeamRole Role { get; set; }
        [JsonPropertyName("allianceGroup")] public int AllianceGroup { get; set; }
        [JsonPropertyName("clanId")] public int? ClanId { get; set; }
        [JsonPropertyName("clanName")] public string? ClanName { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("chatColor")] public string? ChatColor { get; set; }
        [JsonPropertyName("bannerDesignId")] public int? BannerDesignId { get; set; }
        [JsonPropertyName("startMessage")] public string? StartMessage { get; set; }

        // Identity after Clan fallback (DESIGN §3.4) - null where neither the team nor its clan has
        // a value (an incomplete ad-hoc team).
        [JsonPropertyName("resolvedName")] public string? ResolvedName { get; set; }
        [JsonPropertyName("resolvedChatColor")] public string? ResolvedChatColor { get; set; }
        [JsonPropertyName("resolvedBannerDesignId")] public int? ResolvedBannerDesignId { get; set; }

        [JsonPropertyName("spawnpoints")] public List<SiegeSpawnpointReadDto> Spawnpoints { get; set; } = new();
    }

    // Spawnpoints are managed through their own endpoints; a "spawnpoints" property is ignored.
    // SortOrder null on create = after the existing teams; null on update = keep.
    public class SiegeTeamUpsertDto
    {
        [JsonPropertyName("siegeScenarioId")] public int? SiegeScenarioId { get; set; }
        [JsonPropertyName("sortOrder")] public int? SortOrder { get; set; }
        [JsonPropertyName("role")] public SiegeTeamRole Role { get; set; } = SiegeTeamRole.Attacker;
        [JsonPropertyName("allianceGroup")] public int AllianceGroup { get; set; } = 1;
        [JsonPropertyName("clanId")] public int? ClanId { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("chatColor")] public string? ChatColor { get; set; }
        [JsonPropertyName("bannerDesignId")] public int? BannerDesignId { get; set; }
        [JsonPropertyName("startMessage")] public string? StartMessage { get; set; }
    }

    // ---- SiegeSpawnpoint ----

    public class SiegeSpawnpointReadDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("siegeTeamId")] public int SiegeTeamId { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("locationId")] public int LocationId { get; set; }
        [JsonPropertyName("location")] public LocationDto? Location { get; set; }
        [JsonPropertyName("safeZoneRadius")] public double SafeZoneRadius { get; set; }
    }

    // SortOrder null on create = after the existing spawnpoints (0 = the team's default spawn).
    public class SiegeSpawnpointUpsertDto
    {
        [JsonPropertyName("siegeTeamId")] public int? SiegeTeamId { get; set; }
        [JsonPropertyName("sortOrder")] public int? SortOrder { get; set; }
        [Required][JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("locationId")] public int? LocationId { get; set; }
        [JsonPropertyName("location")] public LocationDto? Location { get; set; }
        [JsonPropertyName("safeZoneRadius")] public double SafeZoneRadius { get; set; } = 4;
    }

    // ---- SiegeObjective ----

    public class SiegeObjectiveReadDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("locationId")] public int? LocationId { get; set; }
        [JsonPropertyName("location")] public LocationDto? Location { get; set; }
        [JsonPropertyName("gateStructureId")] public int? GateStructureId { get; set; }
        [JsonPropertyName("gateStructureName")] public string? GateStructureName { get; set; }
        [JsonPropertyName("capturePoints")] public int CapturePoints { get; set; }
        [JsonPropertyName("captureRadius")] public double CaptureRadius { get; set; }
        [JsonPropertyName("instantVictory")] public bool InstantVictory { get; set; }
        [JsonPropertyName("initialHolderTeamId")] public int? InitialHolderTeamId { get; set; }
        [JsonPropertyName("spawnWhenHeld")] public bool SpawnWhenHeld { get; set; }
        [JsonPropertyName("gateStateOnCapture")] public GateDoorOpenState GateStateOnCapture { get; set; }
    }

    public class SiegeObjectiveUpsertDto
    {
        [JsonPropertyName("siegeScenarioId")] public int? SiegeScenarioId { get; set; }
        [JsonPropertyName("sortOrder")] public int? SortOrder { get; set; }
        [Required][JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("locationId")] public int? LocationId { get; set; }
        [JsonPropertyName("location")] public LocationDto? Location { get; set; }
        [JsonPropertyName("gateStructureId")] public int? GateStructureId { get; set; }
        [JsonPropertyName("capturePoints")] public int CapturePoints { get; set; } = 500;
        [JsonPropertyName("captureRadius")] public double CaptureRadius { get; set; } = 2.5;
        [JsonPropertyName("instantVictory")] public bool InstantVictory { get; set; }
        // null (or 0) -> the scenario's first Defender team.
        [JsonPropertyName("initialHolderTeamId")] public int? InitialHolderTeamId { get; set; }
        [JsonPropertyName("spawnWhenHeld")] public bool SpawnWhenHeld { get; set; } = true;
        [JsonPropertyName("gateStateOnCapture")] public GateDoorOpenState GateStateOnCapture { get; set; } = GateDoorOpenState.OPEN;
    }

    // ---- Readiness (§3.9) ----

    public class SiegeScenarioReadinessDto
    {
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }

        // True when there are no errors; warnings never block readiness.
        [JsonPropertyName("isReady")] public bool IsReady { get; set; }

        // False when the WorldGuard-region checks were skipped (runtime-config) or couldn't run
        // (plugin unreachable, town without a region) - see the warnings.
        [JsonPropertyName("spatialChecksRun")] public bool SpatialChecksRun { get; set; }

        [JsonPropertyName("errors")] public List<SiegeReadinessIssueDto> Errors { get; set; } = new();
        [JsonPropertyName("warnings")] public List<SiegeReadinessIssueDto> Warnings { get; set; } = new();
    }

    public class SiegeReadinessIssueDto
    {
        // Stable machine code (SiegeReadinessCodes) for the web-app panel and tests.
        [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;
        [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
        // The row the issue is about ("SiegeTeam", 12), when it is about one.
        [JsonPropertyName("entityType")] public string? EntityType { get; set; }
        [JsonPropertyName("entityId")] public int? EntityId { get; set; }
    }

    // ---- SiegeLobby ----

    public class SiegeLobbyReadDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [JsonPropertyName("isEnabled")] public bool IsEnabled { get; set; }
        [JsonPropertyName("mode")] public SiegeLobbyMode Mode { get; set; }
        [JsonPropertyName("matchmakingSeconds")] public int MatchmakingSeconds { get; set; }
        [JsonPropertyName("cooldownSeconds")] public int CooldownSeconds { get; set; }
        [JsonPropertyName("voteCandidateCount")] public int VoteCandidateCount { get; set; }
        [JsonPropertyName("allowRandomVote")] public bool AllowRandomVote { get; set; }
        [JsonPropertyName("scheduleJson")] public string? ScheduleJson { get; set; }
        [JsonPropertyName("rotation")] public List<SiegeLobbyScenarioDto> Rotation { get; set; } = new();
    }

    // Rotation is an M2M join replaced as a set; null leaves it unchanged, [] clears it.
    public class SiegeLobbyUpsertDto
    {
        [Required][JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [Required][JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [JsonPropertyName("isEnabled")] public bool IsEnabled { get; set; }
        [JsonPropertyName("mode")] public SiegeLobbyMode Mode { get; set; } = SiegeLobbyMode.Continuous;
        [JsonPropertyName("matchmakingSeconds")] public int MatchmakingSeconds { get; set; } = 300;
        [JsonPropertyName("cooldownSeconds")] public int CooldownSeconds { get; set; } = 900;
        [JsonPropertyName("voteCandidateCount")] public int VoteCandidateCount { get; set; } = 2;
        [JsonPropertyName("allowRandomVote")] public bool AllowRandomVote { get; set; } = true;
        [JsonPropertyName("scheduleJson")] public string? ScheduleJson { get; set; }
        [JsonPropertyName("rotation")] public List<SiegeLobbyScenarioUpsertDto>? Rotation { get; set; }
    }

    public class SiegeLobbyListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [JsonPropertyName("isEnabled")] public bool IsEnabled { get; set; }
        [JsonPropertyName("mode")] public SiegeLobbyMode Mode { get; set; }
        [JsonPropertyName("rotationCount")] public int RotationCount { get; set; }
    }

    public class SiegeLobbyScenarioDto
    {
        [JsonPropertyName("siegeLobbyId")] public int SiegeLobbyId { get; set; }
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("siegeScenarioName")] public string? SiegeScenarioName { get; set; }
        [JsonPropertyName("weight")] public int Weight { get; set; }
    }

    public class SiegeLobbyScenarioUpsertDto
    {
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("weight")] public int Weight { get; set; } = 1;
    }

    // ---- SiegeConfiguration (singleton) ----

    public class SiegeConfigurationDto
    {
        [JsonPropertyName("captureAttackBase")] public int CaptureAttackBase { get; set; }
        [JsonPropertyName("captureAttackPerExtra")] public int CaptureAttackPerExtra { get; set; }
        [JsonPropertyName("captureAttackPerExtraInstantVictory")] public int CaptureAttackPerExtraInstantVictory { get; set; }
        [JsonPropertyName("captureDefendBase")] public int CaptureDefendBase { get; set; }
        [JsonPropertyName("captureDefendPerExtra")] public int CaptureDefendPerExtra { get; set; }
        [JsonPropertyName("captureDefendPerExtraInstantVictory")] public int CaptureDefendPerExtraInstantVictory { get; set; }
        [JsonPropertyName("sideCaptureReduction")] public double SideCaptureReduction { get; set; }
        [JsonPropertyName("voteCloseSecondsBeforeStart")] public int VoteCloseSecondsBeforeStart { get; set; }
        [JsonPropertyName("drawSecondsBeforeStart")] public int DrawSecondsBeforeStart { get; set; }
        [JsonPropertyName("hubSecondsBeforeStart")] public int HubSecondsBeforeStart { get; set; }
        [JsonPropertyName("teamSplitSecondsBeforeStart")] public int TeamSplitSecondsBeforeStart { get; set; }
        [JsonPropertyName("matchmakingAnnouncementMarks")] public List<int> MatchmakingAnnouncementMarks { get; set; } = new();
        [JsonPropertyName("killAnnouncementThresholds")] public List<int> KillAnnouncementThresholds { get; set; } = new();
        [JsonPropertyName("killStreakAnnounceAbove")] public int KillStreakAnnounceAbove { get; set; }
        [JsonPropertyName("headshotMultiplier")] public double HeadshotMultiplier { get; set; }
        [JsonPropertyName("allowedCommands")] public List<string> AllowedCommands { get; set; } = new();
        [JsonPropertyName("spawnPickerDelayTicks")] public int SpawnPickerDelayTicks { get; set; }
        [JsonPropertyName("enchantDropChancePerMille")] public int EnchantDropChancePerMille { get; set; }
        [JsonPropertyName("allowedEnchantmentKeys")] public List<string> AllowedEnchantmentKeys { get; set; } = new();
        [JsonPropertyName("enchantLevelMin")] public int EnchantLevelMin { get; set; }
        [JsonPropertyName("enchantLevelMax")] public int EnchantLevelMax { get; set; }
        [JsonPropertyName("maxBooksAlive")] public int MaxBooksAlive { get; set; }
        [JsonPropertyName("nonMemberGateView")] public SiegeNonMemberGateView NonMemberGateView { get; set; }
        [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; set; }
    }

    // Every property is optional: null keeps the current value, so a partial PUT is enough.
    public class UpdateSiegeConfigurationDto
    {
        [JsonPropertyName("captureAttackBase")] public int? CaptureAttackBase { get; set; }
        [JsonPropertyName("captureAttackPerExtra")] public int? CaptureAttackPerExtra { get; set; }
        [JsonPropertyName("captureAttackPerExtraInstantVictory")] public int? CaptureAttackPerExtraInstantVictory { get; set; }
        [JsonPropertyName("captureDefendBase")] public int? CaptureDefendBase { get; set; }
        [JsonPropertyName("captureDefendPerExtra")] public int? CaptureDefendPerExtra { get; set; }
        [JsonPropertyName("captureDefendPerExtraInstantVictory")] public int? CaptureDefendPerExtraInstantVictory { get; set; }
        [JsonPropertyName("sideCaptureReduction")] public double? SideCaptureReduction { get; set; }
        [JsonPropertyName("voteCloseSecondsBeforeStart")] public int? VoteCloseSecondsBeforeStart { get; set; }
        [JsonPropertyName("drawSecondsBeforeStart")] public int? DrawSecondsBeforeStart { get; set; }
        [JsonPropertyName("hubSecondsBeforeStart")] public int? HubSecondsBeforeStart { get; set; }
        [JsonPropertyName("teamSplitSecondsBeforeStart")] public int? TeamSplitSecondsBeforeStart { get; set; }
        [JsonPropertyName("matchmakingAnnouncementMarks")] public List<int>? MatchmakingAnnouncementMarks { get; set; }
        [JsonPropertyName("killAnnouncementThresholds")] public List<int>? KillAnnouncementThresholds { get; set; }
        [JsonPropertyName("killStreakAnnounceAbove")] public int? KillStreakAnnounceAbove { get; set; }
        [JsonPropertyName("headshotMultiplier")] public double? HeadshotMultiplier { get; set; }
        [JsonPropertyName("allowedCommands")] public List<string>? AllowedCommands { get; set; }
        [JsonPropertyName("spawnPickerDelayTicks")] public int? SpawnPickerDelayTicks { get; set; }
        [JsonPropertyName("enchantDropChancePerMille")] public int? EnchantDropChancePerMille { get; set; }
        [JsonPropertyName("allowedEnchantmentKeys")] public List<string>? AllowedEnchantmentKeys { get; set; }
        [JsonPropertyName("enchantLevelMin")] public int? EnchantLevelMin { get; set; }
        [JsonPropertyName("enchantLevelMax")] public int? EnchantLevelMax { get; set; }
        [JsonPropertyName("maxBooksAlive")] public int? MaxBooksAlive { get; set; }
        [JsonPropertyName("nonMemberGateView")] public SiegeNonMemberGateView? NonMemberGateView { get; set; }
    }

    // ---- Runtime config (GET /api/siege-lobbies/runtime-config, DESIGN §11.2) ----
    // One payload for the plugin's cache: the global configuration, every enabled lobby with its
    // rotation, and each rotation scenario fully resolved (team identity from Clans, "first Defender"
    // defaults applied, objective capture points resolved from gates). Only ready scenarios are
    // included; the rest are listed in SkippedScenarios with their readiness errors.

    public class SiegeRuntimeConfigDto
    {
        [JsonPropertyName("generatedAt")] public DateTime GeneratedAt { get; set; }
        [JsonPropertyName("configuration")] public SiegeConfigurationDto Configuration { get; set; } = new();
        [JsonPropertyName("lobbies")] public List<SiegeRuntimeLobbyDto> Lobbies { get; set; } = new();
    }

    public class SiegeRuntimeLobbyDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [JsonPropertyName("mode")] public SiegeLobbyMode Mode { get; set; }
        [JsonPropertyName("matchmakingSeconds")] public int MatchmakingSeconds { get; set; }
        [JsonPropertyName("cooldownSeconds")] public int CooldownSeconds { get; set; }
        [JsonPropertyName("voteCandidateCount")] public int VoteCandidateCount { get; set; }
        [JsonPropertyName("allowRandomVote")] public bool AllowRandomVote { get; set; }
        [JsonPropertyName("rotation")] public List<SiegeRuntimeRotationEntryDto> Rotation { get; set; } = new();
        [JsonPropertyName("skippedScenarios")] public List<SiegeRuntimeSkippedScenarioDto> SkippedScenarios { get; set; } = new();
    }

    public class SiegeRuntimeRotationEntryDto
    {
        [JsonPropertyName("weight")] public int Weight { get; set; }
        [JsonPropertyName("scenario")] public SiegeRuntimeScenarioDto Scenario { get; set; } = new();
    }

    public class SiegeRuntimeSkippedScenarioDto
    {
        [JsonPropertyName("siegeScenarioId")] public int SiegeScenarioId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("errors")] public List<SiegeReadinessIssueDto> Errors { get; set; } = new();
    }

    public class SiegeRuntimeScenarioDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("townId")] public int TownId { get; set; }
        [JsonPropertyName("townName")] public string? TownName { get; set; }
        [JsonPropertyName("townWgRegionId")] public string? TownWgRegionId { get; set; }
        [JsonPropertyName("districts")] public List<SiegeRuntimeDistrictDto> Districts { get; set; } = new();
        [JsonPropertyName("hubLocation")] public LocationDto? HubLocation { get; set; }
        [JsonPropertyName("playersMin")] public int PlayersMin { get; set; }
        [JsonPropertyName("playersMax")] public int PlayersMax { get; set; }
        [JsonPropertyName("minTitleBracketId")] public int? MinTitleBracketId { get; set; }
        // The bracket's MinExperience, so the plugin can check entry without a second lookup.
        [JsonPropertyName("minTitleExperience")] public int? MinTitleExperience { get; set; }
        [JsonPropertyName("matchDurationMinSeconds")] public int MatchDurationMinSeconds { get; set; }
        [JsonPropertyName("matchDurationPerPlayerSeconds")] public int MatchDurationPerPlayerSeconds { get; set; }
        [JsonPropertyName("matchDurationMaxSeconds")] public int MatchDurationMaxSeconds { get; set; }
        [JsonPropertyName("coinRewardWin")] public int CoinRewardWin { get; set; }
        [JsonPropertyName("expRewardWin")] public int ExpRewardWin { get; set; }
        [JsonPropertyName("gemRewardWin")] public int GemRewardWin { get; set; }
        [JsonPropertyName("coinRewardHolding")] public int CoinRewardHolding { get; set; }
        [JsonPropertyName("expRewardHolding")] public int ExpRewardHolding { get; set; }
        [JsonPropertyName("coinRewardCapture")] public int CoinRewardCapture { get; set; }
        [JsonPropertyName("expRewardCapture")] public int ExpRewardCapture { get; set; }
        [JsonPropertyName("lockdownScenarioArea")] public bool LockdownScenarioArea { get; set; }
        [JsonPropertyName("allowRecapture")] public bool AllowRecapture { get; set; }
        [JsonPropertyName("enchantDropsEnabled")] public bool EnchantDropsEnabled { get; set; }
        [JsonPropertyName("teams")] public List<SiegeRuntimeTeamDto> Teams { get; set; } = new();
        [JsonPropertyName("objectives")] public List<SiegeRuntimeObjectiveDto> Objectives { get; set; } = new();
        [JsonPropertyName("gates")] public List<SiegeRuntimeGateDto> Gates { get; set; } = new();
        // Siege Phase 7 (DESIGN §8.1): the other gate structures in the scenario area (its districts,
        // or the whole town when it has none) - forced open and invincible for the match.
        [JsonPropertyName("areaGateStructureIds")] public List<int> AreaGateStructureIds { get; set; } = new();
    }

    public class SiegeRuntimeDistrictDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("wgRegionId")] public string? WgRegionId { get; set; }
    }

    public class SiegeRuntimeTeamDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        [JsonPropertyName("role")] public SiegeTeamRole Role { get; set; }
        [JsonPropertyName("allianceGroup")] public int AllianceGroup { get; set; }
        [JsonPropertyName("clanId")] public int? ClanId { get; set; }
        // Resolved identity (team value, else the Clan's).
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("chatColor")] public string ChatColor { get; set; } = "WHITE";
        [JsonPropertyName("bannerDesign")] public BannerDesignReadDto? BannerDesign { get; set; }
        [JsonPropertyName("startMessage")] public string? StartMessage { get; set; }
        [JsonPropertyName("spawnpoints")] public List<SiegeRuntimeSpawnpointDto> Spawnpoints { get; set; } = new();
    }

    public class SiegeRuntimeSpawnpointDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("location")] public LocationDto? Location { get; set; }
        [JsonPropertyName("safeZoneRadius")] public double SafeZoneRadius { get; set; }
    }

    public class SiegeRuntimeObjectiveDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        // The objective's own location, else its gate structure's.
        [JsonPropertyName("captureLocation")] public LocationDto? CaptureLocation { get; set; }
        [JsonPropertyName("gateStructureId")] public int? GateStructureId { get; set; }
        [JsonPropertyName("capturePoints")] public int CapturePoints { get; set; }
        [JsonPropertyName("captureRadius")] public double CaptureRadius { get; set; }
        [JsonPropertyName("instantVictory")] public bool InstantVictory { get; set; }
        // Explicit holder, else the scenario's first Defender team.
        [JsonPropertyName("initialHolderTeamId")] public int InitialHolderTeamId { get; set; }
        [JsonPropertyName("spawnWhenHeld")] public bool SpawnWhenHeld { get; set; }
        [JsonPropertyName("gateStateOnCapture")] public GateDoorOpenState GateStateOnCapture { get; set; }
    }

    public class SiegeRuntimeGateDto
    {
        [JsonPropertyName("gateStructureId")] public int GateStructureId { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        // Explicit owner, else the scenario's first Defender team.
        [JsonPropertyName("initialOwnerTeamId")] public int InitialOwnerTeamId { get; set; }
        [JsonPropertyName("initialState")] public GateDoorOpenState InitialState { get; set; }
        [JsonPropertyName("damageable")] public bool Damageable { get; set; }
        // Referenced by one of the scenario's objectives (DESIGN §3.7).
        [JsonPropertyName("isObjectiveGate")] public bool IsObjectiveGate { get; set; }
    }
}
