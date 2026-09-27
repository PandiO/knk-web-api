using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    // Domain discovery (docs/specs/domain-discovery/DESIGN.md §3.5).

    /// <summary>
    /// Body of POST api/users/{userId}/discoveries. The plugin sends the raw WorldGuard region ids
    /// the player entered (and/or domain ids); the server resolves them, adds ancestors and computes
    /// every amount - the caller never sends amounts. At most 50 ids in total.
    /// </summary>
    public class DiscoveryGrantRequestDto
    {
        [JsonPropertyName("wgRegionIds")]
        public List<string>? WgRegionIds { get; set; }

        [JsonPropertyName("domainIds")]
        public List<int>? DomainIds { get; set; }

        /// <summary>"RegionEnter", "JoinInside" or "Replay". Defaults to RegionEnter.</summary>
        [JsonPropertyName("source")]
        public string? Source { get; set; }
    }

    /// <summary>
    /// Result of a discovery grant. Idempotent: a repeat of the same request grants nothing and
    /// lists the domains under <see cref="AlreadyDiscovered"/>.
    ///
    /// Amounts come as base (before multipliers) and granted (after), with the multipliers that
    /// were applied to each currency, the same breakdown KNG-16 gives salary and title bonuses so
    /// the plugin can show "Discovered Rivia: 2,600 x1.2 Royal = +3,120 coins".
    /// </summary>
    public class DiscoveryGrantResultDto
    {
        /// <summary>Newly discovered domains, top-down (Town, District, Structure, GateStructure,
        /// then id) - the order to show them in.</summary>
        [JsonPropertyName("granted")]
        public List<DiscoveryGrantDto> Granted { get; set; } = new();

        /// <summary>Domain ids among the request (and their ancestors) the user had already discovered.</summary>
        [JsonPropertyName("alreadyDiscovered")]
        public List<int> AlreadyDiscovered { get; set; } = new();

        [JsonPropertyName("skipped")]
        public List<DiscoverySkipDto> Skipped { get; set; } = new();

        [JsonPropertyName("totalCoins")]
        public int TotalCoins { get; set; }

        [JsonPropertyName("totalGems")]
        public int TotalGems { get; set; }

        [JsonPropertyName("totalExp")]
        public int TotalExp { get; set; }

        /// <summary>Sum of the granted domains' amounts before multipliers.</summary>
        [JsonPropertyName("totalCoinsBase")]
        public int TotalCoinsBase { get; set; }

        [JsonPropertyName("totalGemsBase")]
        public int TotalGemsBase { get; set; }

        [JsonPropertyName("totalExpBase")]
        public int TotalExpBase { get; set; }

        /// <summary>Applied to every granted domain's coins: personal salary multiplier first, then
        /// one per active rank (KNG-16 RewardMultiplierDto).</summary>
        [JsonPropertyName("coinMultipliers")]
        public List<RewardMultiplierDto> CoinMultipliers { get; set; } = new();

        /// <summary>Personal and rank GemBonus multipliers.</summary>
        [JsonPropertyName("gemMultipliers")]
        public List<RewardMultiplierDto> GemMultipliers { get; set; } = new();

        /// <summary>Personal and rank ExpBonus multipliers.</summary>
        [JsonPropertyName("expMultipliers")]
        public List<RewardMultiplierDto> ExpMultipliers { get; set; } = new();

        /// <summary>The title bracket that scaled the rewards (null when none was granted or no
        /// brackets exist).</summary>
        [JsonPropertyName("titleBracketId")]
        public int? TitleBracketId { get; set; }

        /// <summary>Balances after the grant (unchanged when nothing was granted).</summary>
        [JsonPropertyName("newCoins")]
        public int NewCoins { get; set; }

        [JsonPropertyName("newGems")]
        public int NewGems { get; set; }

        [JsonPropertyName("newExperiencePoints")]
        public int NewExperiencePoints { get; set; }

        /// <summary>Set when the discovery XP crossed a title bracket - one consolidated change,
        /// with its own bonus breakdown. Not queued for the notification poller: the plugin shows
        /// it from this response.</summary>
        [JsonPropertyName("titleChange")]
        public TitleChangeResultDto? TitleChange { get; set; }
    }

    public class DiscoveryGrantDto
    {
        [JsonPropertyName("domainId")]
        public int DomainId { get; set; }

        [JsonPropertyName("wgRegionId")]
        public string? WgRegionId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>Town, District, Structure or GateStructure.</summary>
        [JsonPropertyName("domainType")]
        public string DomainType { get; set; } = "";

        /// <summary>The Town a District or Structure lies in; null for a Town.</summary>
        [JsonPropertyName("parentName")]
        public string? ParentName { get; set; }

        /// <summary>RegionEnter, JoinInside, Replay, or Ancestor when it was discovered with a child.</summary>
        [JsonPropertyName("source")]
        public string Source { get; set; } = "";

        [JsonPropertyName("coins")]
        public int Coins { get; set; }

        [JsonPropertyName("gems")]
        public int Gems { get; set; }

        [JsonPropertyName("exp")]
        public int Exp { get; set; }

        /// <summary>The same amounts before multipliers.</summary>
        [JsonPropertyName("coinsBase")]
        public int CoinsBase { get; set; }

        [JsonPropertyName("gemsBase")]
        public int GemsBase { get; set; }

        [JsonPropertyName("expBase")]
        public int ExpBase { get; set; }
    }

    public class DiscoverySkipDto
    {
        public const string NotADomain = "NotADomain";
        public const string Disabled = "Disabled";
        public const string RateLimited = "RateLimited";

        /// <summary>The region id as sent, or the domain id as a string.</summary>
        [JsonPropertyName("key")]
        public string Key { get; set; } = "";

        /// <summary>NotADomain, Disabled or RateLimited.</summary>
        [JsonPropertyName("reason")]
        public string Reason { get; set; } = "";
    }

    /// <summary>One discovered domain, for the plugin's known-set cache.</summary>
    public class KnownDiscoveryDto
    {
        [JsonPropertyName("domainId")]
        public int DomainId { get; set; }

        [JsonPropertyName("wgRegionId")]
        public string? WgRegionId { get; set; }
    }

    /// <summary>One discoverable domain and whether the user has found it (discoveries menu, web).</summary>
    public class DiscoveryProgressRowDto
    {
        [JsonPropertyName("domainId")]
        public int DomainId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("domainType")]
        public string DomainType { get; set; } = "";

        [JsonPropertyName("parentName")]
        public string? ParentName { get; set; }

        [JsonPropertyName("discovered")]
        public bool Discovered { get; set; }

        [JsonPropertyName("discoveredAt")]
        public DateTime? DiscoveredAt { get; set; }

        [JsonPropertyName("coins")]
        public int Coins { get; set; }

        [JsonPropertyName("gems")]
        public int Gems { get; set; }

        [JsonPropertyName("exp")]
        public int Exp { get; set; }
    }

    public class DiscoverySummaryDto
    {
        /// <summary>Per discoverable type, top-down: discovered vs. total enabled domains.</summary>
        [JsonPropertyName("byType")]
        public List<DiscoveryTypeCountDto> ByType { get; set; } = new();

        [JsonPropertyName("latest")]
        public DiscoveryProgressRowDto? Latest { get; set; }

        [JsonPropertyName("totalDiscovered")]
        public int TotalDiscovered { get; set; }

        /// <summary>Lifetime amounts credited by discoveries.</summary>
        [JsonPropertyName("totalCoins")]
        public int TotalCoins { get; set; }

        [JsonPropertyName("totalGems")]
        public int TotalGems { get; set; }

        [JsonPropertyName("totalExp")]
        public int TotalExp { get; set; }
    }

    public class DiscoveryTypeCountDto
    {
        [JsonPropertyName("domainType")]
        public string DomainType { get; set; } = "";

        [JsonPropertyName("discovered")]
        public int Discovered { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

    /// <summary>A domain type's reward rule (GET/PUT api/discovery-rewards).</summary>
    public class DiscoveryRewardRuleDto
    {
        [JsonPropertyName("domainType")]
        public string DomainType { get; set; } = "";

        [JsonPropertyName("isEnabled")]
        public bool IsEnabled { get; set; }

        [JsonPropertyName("expUnitsMin")]
        public decimal ExpUnitsMin { get; set; }

        [JsonPropertyName("expUnitsMax")]
        public decimal ExpUnitsMax { get; set; }

        [JsonPropertyName("coinSalaryHoursMin")]
        public decimal CoinSalaryHoursMin { get; set; }

        [JsonPropertyName("coinSalaryHoursMax")]
        public decimal CoinSalaryHoursMax { get; set; }

        [JsonPropertyName("gemsMin")]
        public int GemsMin { get; set; }

        [JsonPropertyName("gemsMax")]
        public int GemsMax { get; set; }

        [JsonPropertyName("includeAncestors")]
        public bool IncludeAncestors { get; set; }

        [JsonPropertyName("updatedAt")]
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>Body of PUT api/discovery-rewards/{domainType}. Every min must be at most its max,
    /// and nothing negative.</summary>
    public class UpdateDiscoveryRewardRuleDto
    {
        [JsonPropertyName("isEnabled")]
        public bool IsEnabled { get; set; } = true;

        [JsonPropertyName("expUnitsMin")]
        public decimal ExpUnitsMin { get; set; }

        [JsonPropertyName("expUnitsMax")]
        public decimal ExpUnitsMax { get; set; }

        [JsonPropertyName("coinSalaryHoursMin")]
        public decimal CoinSalaryHoursMin { get; set; }

        [JsonPropertyName("coinSalaryHoursMax")]
        public decimal CoinSalaryHoursMax { get; set; }

        [JsonPropertyName("gemsMin")]
        public int GemsMin { get; set; }

        [JsonPropertyName("gemsMax")]
        public int GemsMax { get; set; }

        [JsonPropertyName("includeAncestors")]
        public bool IncludeAncestors { get; set; }
    }

    /// <summary>A per-domain override. Null fields inherit the type rule.</summary>
    public class DomainDiscoveryOverrideDto
    {
        [JsonPropertyName("domainId")]
        public int DomainId { get; set; }

        [JsonPropertyName("domainName")]
        public string? DomainName { get; set; }

        [JsonPropertyName("domainType")]
        public string? DomainType { get; set; }

        [JsonPropertyName("isEnabled")]
        public bool? IsEnabled { get; set; }

        [JsonPropertyName("expUnitsMin")]
        public decimal? ExpUnitsMin { get; set; }

        [JsonPropertyName("expUnitsMax")]
        public decimal? ExpUnitsMax { get; set; }

        [JsonPropertyName("coinSalaryHoursMin")]
        public decimal? CoinSalaryHoursMin { get; set; }

        [JsonPropertyName("coinSalaryHoursMax")]
        public decimal? CoinSalaryHoursMax { get; set; }

        [JsonPropertyName("gemsMin")]
        public int? GemsMin { get; set; }

        [JsonPropertyName("gemsMax")]
        public int? GemsMax { get; set; }

        [JsonPropertyName("includeAncestors")]
        public bool? IncludeAncestors { get; set; }

        [JsonPropertyName("updatedAt")]
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>Body of PUT api/discovery-rewards/overrides/{domainId}.</summary>
    public class UpdateDomainDiscoveryOverrideDto
    {
        [JsonPropertyName("isEnabled")]
        public bool? IsEnabled { get; set; }

        [JsonPropertyName("expUnitsMin")]
        public decimal? ExpUnitsMin { get; set; }

        [JsonPropertyName("expUnitsMax")]
        public decimal? ExpUnitsMax { get; set; }

        [JsonPropertyName("coinSalaryHoursMin")]
        public decimal? CoinSalaryHoursMin { get; set; }

        [JsonPropertyName("coinSalaryHoursMax")]
        public decimal? CoinSalaryHoursMax { get; set; }

        [JsonPropertyName("gemsMin")]
        public int? GemsMin { get; set; }

        [JsonPropertyName("gemsMax")]
        public int? GemsMax { get; set; }

        [JsonPropertyName("includeAncestors")]
        public bool? IncludeAncestors { get; set; }
    }

    /// <summary>GET api/discovery-rewards/preview: what a domain type (or one domain, with its
    /// override) pays at each title, at multiplier 1.0.</summary>
    public class DiscoveryRewardPreviewDto
    {
        [JsonPropertyName("domainType")]
        public string DomainType { get; set; } = "";

        [JsonPropertyName("domainId")]
        public int? DomainId { get; set; }

        /// <summary>The effective rule the rows were computed from (override applied).</summary>
        [JsonPropertyName("rule")]
        public DiscoveryRewardRuleDto Rule { get; set; } = new();

        [JsonPropertyName("rows")]
        public List<DiscoveryRewardPreviewRowDto> Rows { get; set; } = new();
    }

    public class DiscoveryRewardPreviewRowDto
    {
        [JsonPropertyName("titleBracketId")]
        public int TitleBracketId { get; set; }

        [JsonPropertyName("titleName")]
        public string TitleName { get; set; } = "";

        [JsonPropertyName("minExperience")]
        public int MinExperience { get; set; }

        /// <summary>1% of the bracket's XP width (v1 getExpPart unit).</summary>
        [JsonPropertyName("expUnit")]
        public int ExpUnit { get; set; }

        [JsonPropertyName("salary")]
        public int Salary { get; set; }

        [JsonPropertyName("expMin")]
        public int ExpMin { get; set; }

        [JsonPropertyName("expMax")]
        public int ExpMax { get; set; }

        [JsonPropertyName("coinsMin")]
        public int CoinsMin { get; set; }

        [JsonPropertyName("coinsMax")]
        public int CoinsMax { get; set; }

        [JsonPropertyName("gemsMin")]
        public int GemsMin { get; set; }

        [JsonPropertyName("gemsMax")]
        public int GemsMax { get; set; }
    }

    /// <summary>GET api/discoveries/stats.</summary>
    public class DiscoveryStatsDto
    {
        /// <summary>Discoverable domains with their discoverer counts, most discovered first
        /// (sortDescending=false: least discovered first).</summary>
        [JsonPropertyName("domains")]
        public PagedResultDto<DomainDiscoveryStatDto> Domains { get; set; } = new();

        /// <summary>Active users with a linked Minecraft account - the denominator of
        /// <see cref="DomainDiscoveryStatDto.DiscovererPercent"/>.</summary>
        [JsonPropertyName("linkedUserCount")]
        public int LinkedUserCount { get; set; }

        [JsonPropertyName("topExplorers")]
        public List<DiscoveryExplorerDto> TopExplorers { get; set; } = new();
    }

    public class DomainDiscoveryStatDto
    {
        [JsonPropertyName("domainId")]
        public int DomainId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("domainType")]
        public string DomainType { get; set; } = "";

        [JsonPropertyName("parentName")]
        public string? ParentName { get; set; }

        [JsonPropertyName("discoverers")]
        public int Discoverers { get; set; }

        [JsonPropertyName("discovererPercent")]
        public decimal DiscovererPercent { get; set; }

        [JsonPropertyName("firstDiscovererUserId")]
        public int? FirstDiscovererUserId { get; set; }

        [JsonPropertyName("firstDiscovererUsername")]
        public string? FirstDiscovererUsername { get; set; }

        [JsonPropertyName("firstDiscoveredAt")]
        public DateTime? FirstDiscoveredAt { get; set; }
    }

    public class DiscoveryExplorerDto
    {
        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("discoveries")]
        public int Discoveries { get; set; }
    }
}
