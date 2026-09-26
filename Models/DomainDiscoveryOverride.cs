using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// Per-domain change to its type's <see cref="DiscoveryRewardRule"/>
/// (docs/specs/domain-discovery/DESIGN.md §3.1). Every field is nullable: null inherits the type
/// rule's value, so one structure can be made undiscoverable or a landmark worth more.
/// </summary>
public class DomainDiscoveryOverride
{
    public int DomainId { get; set; }
    public Domain Domain { get; set; } = null!;

    public bool? IsEnabled { get; set; }
    public decimal? ExpUnitsMin { get; set; }
    public decimal? ExpUnitsMax { get; set; }
    public decimal? CoinSalaryHoursMin { get; set; }
    public decimal? CoinSalaryHoursMax { get; set; }
    public int? GemsMin { get; set; }
    public int? GemsMax { get; set; }
    public bool? IncludeAncestors { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
