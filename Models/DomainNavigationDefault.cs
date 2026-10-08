using System;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// The <c>/navigate</c> default of one domain type (KNG-73, docs/specs/navigation/DESIGN.md §6.1),
/// keyed by the CLR type name DomainListDto.DomainType already uses: Town, District, Structure,
/// GateStructure. Seeded with Spawn by the AddDomainNavigationDefaults migration; a domain's own
/// <see cref="Domain.NavigationDefaultOverride"/> wins over it.
/// </summary>
public class DomainNavigationDefault
{
    public const string Town = nameof(Models.Town);
    public const string District = nameof(Models.District);
    public const string Structure = nameof(Models.Structure);
    public const string GateStructure = nameof(Models.GateStructure);

    /// <summary>Every navigable domain type, top-down.</summary>
    public static readonly string[] DomainTypes = { Town, District, Structure, GateStructure };

    /// <summary>A type without a row, or a domain of another type, goes to its spawn Location (the behaviour before KNG-73).</summary>
    public const NavigationDestinationMode Fallback = NavigationDestinationMode.Spawn;

    public string DomainType { get; set; } = null!;

    public NavigationDestinationMode DefaultMode { get; set; } = Fallback;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
