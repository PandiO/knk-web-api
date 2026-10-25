using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using knkwebapi_v2.Json;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// An Arena (arena duels DESIGN.md §4.1–4.2, KNG-210). On create/update a null duel setting keeps the current value
    /// (the design default on create), so a client that only knows some of them can't reset the rest.
    /// </summary>
    public class ArenaDto : IDomainTeleportSettingsDto, IDomainNavigationDefaultDto
    {
        // ===== Domain / Structure =====
        [JsonPropertyName("id")] [JsonConverter(typeof(NullableIntConverter))] public int? Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = null!;
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("createdAt")] public DateTime? CreatedAt { get; set; }
        [JsonPropertyName("allowEntry")] [JsonConverter(typeof(NullableBoolConverter))] public bool? AllowEntry { get; set; }
        [JsonPropertyName("allowExit")] [JsonConverter(typeof(NullableBoolConverter))] public bool? AllowExit { get; set; }
        [JsonPropertyName("wgRegionId")] public string WgRegionId { get; set; } = null!;
        [JsonPropertyName("worldName")] public string? WorldName { get; set; }
        [JsonPropertyName("locationId")] [JsonConverter(typeof(NullableIntConverter))] public int? LocationId { get; set; }
        [JsonPropertyName("teleportEnabled")] [JsonConverter(typeof(NullableBoolConverter))] public bool? TeleportEnabled { get; set; }
        [JsonPropertyName("teleportPriceGems")] [JsonConverter(typeof(NullableIntConverter))] public int? TeleportPriceGems { get; set; }
        [JsonPropertyName("teleportMinTitleBracketId")] [JsonConverter(typeof(NullableIntConverter))] public int? TeleportMinTitleBracketId { get; set; }
        [JsonPropertyName("teleportMinPremiumGroupId")] [JsonConverter(typeof(NullableIntConverter))] public int? TeleportMinPremiumGroupId { get; set; }
        [JsonPropertyName("teleportRequiresDiscovery")] [JsonConverter(typeof(NullableBoolConverter))] public bool? TeleportRequiresDiscovery { get; set; }
        [JsonPropertyName("navigationDefaultOverride")] public string? NavigationDefaultOverride { get; set; }
        [JsonPropertyName("roadAccessOverride")] public string? RoadAccessOverride { get; set; }
        [JsonPropertyName("streetId")] [JsonConverter(typeof(NullableIntConverter))] public int? StreetId { get; set; }
        [JsonPropertyName("districtId")] [JsonConverter(typeof(NullableIntConverter))] public int? DistrictId { get; set; }
        // Optional on the arena form (like GateStructure): null keeps 0.
        [JsonPropertyName("houseNumber")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? HouseNumber { get; set; }
        [JsonPropertyName("street")] public StructureStreetDto? Street { get; set; }
        [JsonPropertyName("district")] public StructureDistrictDto? District { get; set; }

        // ===== Arena =====
        [JsonPropertyName("key")]
        public string Key { get; set; } = null!;

        [JsonPropertyName("exitLocationId")]
        public int? ExitLocationId { get; set; }

        // ===== General =====
        [JsonPropertyName("isDuelEnabled")] [JsonConverter(typeof(NullableBoolConverter))] public bool? IsDuelEnabled { get; set; }
        [JsonPropertyName("maxQueuedMatches")] [JsonConverter(typeof(NullableIntConverter))] public int? MaxQueuedMatches { get; set; }

        // ===== Negotiation =====
        [JsonPropertyName("maxOffers")] [JsonConverter(typeof(NullableIntConverter))] public int? MaxOffers { get; set; }
        [JsonPropertyName("offerSeconds")] [JsonConverter(typeof(NullableIntConverter))] public int? OfferSeconds { get; set; }

        // ===== Stakes =====
        [JsonPropertyName("allowCoinStakes")] [JsonConverter(typeof(NullableBoolConverter))] public bool? AllowCoinStakes { get; set; }
        [JsonPropertyName("allowItemStakes")] [JsonConverter(typeof(NullableBoolConverter))] public bool? AllowItemStakes { get; set; }
        [JsonPropertyName("maxCoinStake")] [JsonConverter(typeof(NullableIntConverter))] public int? MaxCoinStake { get; set; }
        [JsonPropertyName("maxItemStacks")] [JsonConverter(typeof(NullableIntConverter))] public int? MaxItemStacks { get; set; }
        [JsonPropertyName("minStakeRatioPercent")] [JsonConverter(typeof(NullableIntConverter))] public int? MinStakeRatioPercent { get; set; }
        [JsonPropertyName("feePercent")] [JsonConverter(typeof(NullableIntConverter))] public int? FeePercent { get; set; }

        // ===== Eligibility: null = the /pay rules (CurrencyPolicy) =====
        [JsonPropertyName("stakeMinAccountAgeHours")] [JsonConverter(typeof(NullableIntConverter))] public int? StakeMinAccountAgeHours { get; set; }
        [JsonPropertyName("stakeMinTitleBracketId")] [JsonConverter(typeof(NullableIntConverter))] public int? StakeMinTitleBracketId { get; set; }

        // ===== Abuse limits =====
        [JsonPropertyName("maxStakedDuelsPerPairPerDay")] [JsonConverter(typeof(NullableIntConverter))] public int? MaxStakedDuelsPerPairPerDay { get; set; }
        [JsonPropertyName("pairCooldownMinutes")] [JsonConverter(typeof(NullableIntConverter))] public int? PairCooldownMinutes { get; set; }
        [JsonPropertyName("dailyCoinWinCap")] [JsonConverter(typeof(NullableIntConverter))] public int? DailyCoinWinCap { get; set; }

        // ===== Gear =====
        [JsonPropertyName("allowOwnGearKeep")] [JsonConverter(typeof(NullableBoolConverter))] public bool? AllowOwnGearKeep { get; set; }
        [JsonPropertyName("allowOwnGearDrop")] [JsonConverter(typeof(NullableBoolConverter))] public bool? AllowOwnGearDrop { get; set; }
        [JsonPropertyName("allowFixedKit")] [JsonConverter(typeof(NullableBoolConverter))] public bool? AllowFixedKit { get; set; }
        [JsonPropertyName("allowKitChoice")] [JsonConverter(typeof(NullableBoolConverter))] public bool? AllowKitChoice { get; set; }
        /// <summary>OWN_GEAR_KEEP, OWN_GEAR_DROP, FIXED_KIT or KIT_CHOICE.</summary>
        [JsonPropertyName("defaultGearMode")] public string? DefaultGearMode { get; set; }

        // ===== Timing =====
        [JsonPropertyName("warmupSeconds")] [JsonConverter(typeof(NullableIntConverter))] public int? WarmupSeconds { get; set; }
        [JsonPropertyName("countdownSeconds")] [JsonConverter(typeof(NullableIntConverter))] public int? CountdownSeconds { get; set; }
        [JsonPropertyName("fightTimeLimitSeconds")] [JsonConverter(typeof(NullableIntConverter))] public int? FightTimeLimitSeconds { get; set; }
        [JsonPropertyName("maxWaitSeconds")] [JsonConverter(typeof(NullableIntConverter))] public int? MaxWaitSeconds { get; set; }
        [JsonPropertyName("winnerLingerSeconds")] [JsonConverter(typeof(NullableIntConverter))] public int? WinnerLingerSeconds { get; set; }

        // ===== Announcements =====
        [JsonPropertyName("announceMatches")] [JsonConverter(typeof(NullableBoolConverter))] public bool? AnnounceMatches { get; set; }
        [JsonPropertyName("announceFriendly")] [JsonConverter(typeof(NullableBoolConverter))] public bool? AnnounceFriendly { get; set; }

        /// <summary>Replace-set in list order (the first is the KIT_CHOICE default); null = unchanged.</summary>
        [JsonPropertyName("eligibleKits")] public List<ArenaKitDto>? EligibleKits { get; set; }

        // ===== Read-only =====
        [JsonPropertyName("location")] public LocationDto? Location { get; set; }
        [JsonPropertyName("exitLocation")] public LocationDto? ExitLocation { get; set; }
        /// <summary>Rev. 6: battlegrounds, spectator areas and the lobby with their regions (edited through
        /// <c>api/Arenas/areas</c>, the form's owned list), in kind then <c>sortOrder</c> order.</summary>
        [JsonPropertyName("areas")] public List<ArenaAreaDto>? Areas { get; set; }
        /// <summary>KNG-254: the arena domain's own sub-regions (the arena is their union with its main region).</summary>
        [JsonPropertyName("subRegions")] public List<DomainSubRegionDto>? SubRegions { get; set; }
    }

    public class ArenaKitDto
    {
        [JsonPropertyName("arenaId")] [JsonConverter(typeof(NullableIntConverter))] public int? ArenaId { get; set; }
        [JsonPropertyName("kitId")] [JsonConverter(typeof(NullableIntConverter))] public int? KitId { get; set; }
        /// <summary>The list order wins on write (the FormWizard keeps it equal to the row's index).</summary>
        [JsonPropertyName("sequenceNumber")] [JsonConverter(typeof(NullableIntConverter))] public int? SequenceNumber { get; set; }
        [JsonPropertyName("kitName")] public string? KitName { get; set; }
    }

    public class ArenaListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = null!;
        [JsonPropertyName("key")] public string Key { get; set; } = null!;
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("wgRegionId")] public string WgRegionId { get; set; } = null!;
        [JsonPropertyName("worldName")] public string? WorldName { get; set; }
        [JsonPropertyName("isDuelEnabled")] public bool IsDuelEnabled { get; set; }
        [JsonPropertyName("districtId")] public int DistrictId { get; set; }
        [JsonPropertyName("districtName")] public string? DistrictName { get; set; }
        [JsonPropertyName("townName")] public string? TownName { get; set; }
        [JsonPropertyName("battlegroundCount")] public int BattlegroundCount { get; set; }
        [JsonPropertyName("areaCount")] public int AreaCount { get; set; }
    }

    /// <summary>
    /// A battleground, spectator area or lobby (DESIGN.md §4.1, rev. 6): read and write shape of <c>api/Arenas/areas</c>.
    /// Fields of another kind are cleared on save. <c>regions</c> is read-only (edited through
    /// <c>api/Arenas/areas/regions</c>, the area form's owned list).
    /// </summary>
    public class ArenaAreaDto
    {
        [JsonPropertyName("id")] [JsonConverter(typeof(NullableIntConverter))] public int? Id { get; set; }
        [JsonPropertyName("arenaId")] [JsonConverter(typeof(NullableIntConverter))] public int? ArenaId { get; set; }
        /// <summary>BATTLEGROUND, SPECTATOR or LOBBY.</summary>
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("key")] public string Key { get; set; } = null!;
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("sortOrder")] [JsonConverter(typeof(NullableIntConverter))] public int? SortOrder { get; set; }
        [JsonPropertyName("isEnabled")] [JsonConverter(typeof(NullableBoolConverter))] public bool? IsEnabled { get; set; }
        [JsonPropertyName("spawnALocationId")] [JsonConverter(typeof(NullableIntConverter))] public int? SpawnALocationId { get; set; }
        [JsonPropertyName("spawnBLocationId")] [JsonConverter(typeof(NullableIntConverter))] public int? SpawnBLocationId { get; set; }
        [JsonPropertyName("arrivalLocationId")] [JsonConverter(typeof(NullableIntConverter))] public int? ArrivalLocationId { get; set; }
        [JsonPropertyName("teleportMinTitleBracketId")] [JsonConverter(typeof(NullableIntConverter))] public int? TeleportMinTitleBracketId { get; set; }
        [JsonPropertyName("teleportPriceGems")] [JsonConverter(typeof(NullableIntConverter))] public int? TeleportPriceGems { get; set; }
        [JsonPropertyName("createdAt")] public DateTime? CreatedAt { get; set; }

        // ===== Read-only =====
        [JsonPropertyName("arenaName")] public string? ArenaName { get; set; }
        [JsonPropertyName("arenaWgRegionId")] public string? ArenaWgRegionId { get; set; }
        [JsonPropertyName("spawnALocation")] public LocationDto? SpawnALocation { get; set; }
        [JsonPropertyName("spawnBLocation")] public LocationDto? SpawnBLocation { get; set; }
        [JsonPropertyName("arrivalLocation")] public LocationDto? ArrivalLocation { get; set; }
        [JsonPropertyName("teleportMinTitleBracketName")] public string? TeleportMinTitleBracketName { get; set; }
        [JsonPropertyName("regions")] public List<ArenaAreaRegionDto>? Regions { get; set; }
    }

    /// <summary>One region of an arena area (DESIGN.md §4.1, rev. 6): read and write shape of <c>api/Arenas/areas/regions</c>.</summary>
    public class ArenaAreaRegionDto
    {
        [JsonPropertyName("id")] [JsonConverter(typeof(NullableIntConverter))] public int? Id { get; set; }
        [JsonPropertyName("arenaAreaId")] [JsonConverter(typeof(NullableIntConverter))] public int? ArenaAreaId { get; set; }
        /// <summary><c>tempregion_worldtask_&lt;n&gt;</c> from its world task, or an existing region adopted with "select";
        /// <c>arena_&lt;arenaId&gt;_&lt;kind&gt;_&lt;id&gt;</c> once saved.</summary>
        [JsonPropertyName("wgRegionId")] public string WgRegionId { get; set; } = null!;
        /// <summary>Optional on write: the arena's world (a world task's region must be drawn there).</summary>
        [JsonPropertyName("worldName")] public string? WorldName { get; set; }
        [JsonPropertyName("createdAt")] public DateTime? CreatedAt { get; set; }

        // ===== Read-only =====
        [JsonPropertyName("arenaId")] [JsonConverter(typeof(NullableIntConverter))] public int? ArenaId { get; set; }
        [JsonPropertyName("areaKind")] public string? AreaKind { get; set; }
        [JsonPropertyName("areaKey")] public string? AreaKey { get; set; }
    }
}
