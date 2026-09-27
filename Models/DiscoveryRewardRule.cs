using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// Discovery reward settings for one domain type (docs/specs/domain-discovery/DESIGN.md §3.1),
/// keyed by the CLR type name DomainMappingProfile already uses as the domain type string:
/// Town, District, Structure, GateStructure. Seeded by the AddDomainDiscovery migration;
/// <see cref="DomainDiscoveryOverride"/> can change any field for a single domain.
/// </summary>
public class DiscoveryRewardRule
{
    public const string Town = nameof(Models.Town);
    public const string District = nameof(Models.District);
    public const string Structure = nameof(Models.Structure);
    public const string GateStructure = nameof(Models.GateStructure);

    /// <summary>Every discoverable domain type, top-down: the order grants are made and messaged in.</summary>
    public static readonly string[] DomainTypes = { Town, District, Structure, GateStructure };

    public string DomainType { get; set; } = null!;

    public bool IsEnabled { get; set; } = true;

    /// <summary>XP = uniform(min, max) x the player's title XP unit — 1% of their title bracket's
    /// width, v1's getExpPart (DESIGN.md §3.3). Town 1-4 is exactly v1.</summary>
    public decimal ExpUnitsMin { get; set; }
    public decimal ExpUnitsMax { get; set; }

    /// <summary>Coins = uniform(min, max) hours of the player's title Salary.</summary>
    public decimal CoinSalaryHoursMin { get; set; }
    public decimal CoinSalaryHoursMax { get; set; }

    /// <summary>Gems = uniform whole number in [min, max], not title-scaled (v1).</summary>
    public int GemsMin { get; set; }
    public int GemsMax { get; set; }

    /// <summary>Discovering a domain of this type also discovers its parents: a Structure its
    /// District and Town, a District its Town.</summary>
    public bool IncludeAncestors { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
