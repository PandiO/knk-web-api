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

        [JsonPropertyName("lobbyLocationId")]
        public int? LobbyLocationId { get; set; }

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
        [JsonPropertyName("lobbyLocation")] public LocationDto? LobbyLocation { get; set; }
        [JsonPropertyName("exitLocation")] public LocationDto? ExitLocation { get; set; }
        [JsonPropertyName("battlegrounds")] public List<ArenaBattlegroundDto>? Battlegrounds { get; set; }
        [JsonPropertyName("spectatorAreas")] public List<ArenaSpectatorAreaDto>? SpectatorAreas { get; set; }
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
    }

    /// <summary>A battleground (DESIGN.md §4.1). AllowEntry defaults to false on create.</summary>
    public class ArenaBattlegroundDto : IDomainTeleportSettingsDto, IDomainNavigationDefaultDto
    {
        [JsonPropertyName("id")] [JsonConverter(typeof(NullableIntConverter))] public int? Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = null!;
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("createdAt")] public DateTime? CreatedAt { get; set; }
        [JsonPropertyName("allowEntry")] [JsonConverter(typeof(NullableBoolConverter))] public bool? AllowEntry { get; set; }
        [JsonPropertyName("allowExit")] [JsonConverter(typeof(NullableBoolConverter))] public bool? AllowExit { get; set; }
        [JsonPropertyName("wgRegionId")] public string WgRegionId { get; set; } = null!;
        [JsonPropertyName("worldName")] public string? WorldName { get; set; }
        [JsonPropertyName("locationId")] [JsonConverter(typeof(NullableIntConverter))] public int? LocationId { get; set; }

        [JsonPropertyName("arenaId")] [JsonConverter(typeof(NullableIntConverter))] public int? ArenaId { get; set; }
        [JsonPropertyName("key")] public string Key { get; set; } = null!;
        [JsonPropertyName("sortOrder")] [JsonConverter(typeof(NullableIntConverter))] public int? SortOrder { get; set; }
        [JsonPropertyName("isEnabled")] [JsonConverter(typeof(NullableBoolConverter))] public bool? IsEnabled { get; set; }
        [JsonPropertyName("spawnALocationId")] [JsonConverter(typeof(NullableIntConverter))] public int? SpawnALocationId { get; set; }
        [JsonPropertyName("spawnBLocationId")] [JsonConverter(typeof(NullableIntConverter))] public int? SpawnBLocationId { get; set; }

        // Domain warp and navigation fields: a battleground is no warp target, but the shared domain code reads them.
        [JsonPropertyName("teleportEnabled")] [JsonConverter(typeof(NullableBoolConverter))] public bool? TeleportEnabled { get; set; }
        [JsonPropertyName("teleportPriceGems")] [JsonConverter(typeof(NullableIntConverter))] public int? TeleportPriceGems { get; set; }
        [JsonPropertyName("teleportMinTitleBracketId")] [JsonConverter(typeof(NullableIntConverter))] public int? TeleportMinTitleBracketId { get; set; }
        [JsonPropertyName("teleportMinPremiumGroupId")] [JsonConverter(typeof(NullableIntConverter))] public int? TeleportMinPremiumGroupId { get; set; }
        [JsonPropertyName("teleportRequiresDiscovery")] [JsonConverter(typeof(NullableBoolConverter))] public bool? TeleportRequiresDiscovery { get; set; }
        [JsonPropertyName("navigationDefaultOverride")] public string? NavigationDefaultOverride { get; set; }
        [JsonPropertyName("roadAccessOverride")] public string? RoadAccessOverride { get; set; }

        // ===== Read-only =====
        [JsonPropertyName("arenaName")] public string? ArenaName { get; set; }
        [JsonPropertyName("spawnALocation")] public LocationDto? SpawnALocation { get; set; }
        [JsonPropertyName("spawnBLocation")] public LocationDto? SpawnBLocation { get; set; }
    }

    /// <summary>A spectator area (DESIGN.md §4.1, §9.3): an ordinary warpable domain.</summary>
    public class ArenaSpectatorAreaDto : IDomainTeleportSettingsDto, IDomainNavigationDefaultDto
    {
        [JsonPropertyName("id")] [JsonConverter(typeof(NullableIntConverter))] public int? Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = null!;
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("createdAt")] public DateTime? CreatedAt { get; set; }
        [JsonPropertyName("allowEntry")] [JsonConverter(typeof(NullableBoolConverter))] public bool? AllowEntry { get; set; }
        [JsonPropertyName("allowExit")] [JsonConverter(typeof(NullableBoolConverter))] public bool? AllowExit { get; set; }
        [JsonPropertyName("wgRegionId")] public string WgRegionId { get; set; } = null!;
        [JsonPropertyName("worldName")] public string? WorldName { get; set; }
        [JsonPropertyName("locationId")] [JsonConverter(typeof(NullableIntConverter))] public int? LocationId { get; set; }

        [JsonPropertyName("arenaId")] [JsonConverter(typeof(NullableIntConverter))] public int? ArenaId { get; set; }

        [JsonPropertyName("teleportEnabled")] [JsonConverter(typeof(NullableBoolConverter))] public bool? TeleportEnabled { get; set; }
        [JsonPropertyName("teleportPriceGems")] [JsonConverter(typeof(NullableIntConverter))] public int? TeleportPriceGems { get; set; }
        [JsonPropertyName("teleportMinTitleBracketId")] [JsonConverter(typeof(NullableIntConverter))] public int? TeleportMinTitleBracketId { get; set; }
        [JsonPropertyName("teleportMinPremiumGroupId")] [JsonConverter(typeof(NullableIntConverter))] public int? TeleportMinPremiumGroupId { get; set; }
        [JsonPropertyName("teleportRequiresDiscovery")] [JsonConverter(typeof(NullableBoolConverter))] public bool? TeleportRequiresDiscovery { get; set; }
        [JsonPropertyName("navigationDefaultOverride")] public string? NavigationDefaultOverride { get; set; }
        [JsonPropertyName("roadAccessOverride")] public string? RoadAccessOverride { get; set; }

        // ===== Read-only =====
        [JsonPropertyName("arenaName")] public string? ArenaName { get; set; }
        [JsonPropertyName("location")] public LocationDto? Location { get; set; }
    }

    /// <summary>
    /// Creates the v1 arena <c>arena_2</c> and its flagged battlegrounds (KNG-210 seed). The district, street and house
    /// number are picked by staff; the battleground region ids are the arena's <c>-battleground</c> children as WorldGuard
    /// lists them. Locations are set afterwards. Idempotent: an existing arena with the key is completed, not duplicated.
    /// </summary>
    public class ArenaLegacySeedDto
    {
        [JsonPropertyName("key")] public string? Key { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("wgRegionId")] public string? WgRegionId { get; set; }
        [JsonPropertyName("worldName")] public string? WorldName { get; set; }
        [JsonPropertyName("districtId")] public int DistrictId { get; set; }
        [JsonPropertyName("streetId")] [JsonConverter(typeof(NullableIntConverter))] public int? StreetId { get; set; }
        [JsonPropertyName("houseNumber")] [JsonConverter(typeof(NullableIntConverter))] public int? HouseNumber { get; set; }
        [JsonPropertyName("battlegroundRegionIds")] public List<string> BattlegroundRegionIds { get; set; } = new();
    }

    public class ArenaLegacySeedResultDto
    {
        [JsonPropertyName("arena")] public ArenaDto Arena { get; set; } = null!;
        [JsonPropertyName("arenaCreated")] public bool ArenaCreated { get; set; }
        [JsonPropertyName("battlegroundsCreated")] public List<string> BattlegroundsCreated { get; set; } = new();
        [JsonPropertyName("battlegroundsExisting")] public List<string> BattlegroundsExisting { get; set; } = new();
    }
}
