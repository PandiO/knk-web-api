using System;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// A user's permanent first entry into a <see cref="Domain"/> (docs/specs/domain-discovery/DESIGN.md
/// §3.1). Unique per (UserId, DomainId): the row is inserted in the same transaction as the reward
/// credit, so a domain can only ever be rewarded once per account (v1 bug L1 had no such key).
/// </summary>
public class UserDomainDiscovery
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int DomainId { get; set; }
    public Domain Domain { get; set; } = null!;

    /// <summary>Server time (UTC) of the grant.</summary>
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;

    public DiscoverySource Source { get; set; } = DiscoverySource.RegionEnter;

    /// <summary>What this discovery credited, after the player's multipliers (title promotion
    /// bonuses the grant may have triggered are not included).</summary>
    public int CoinsAwarded { get; set; }
    public int GemsAwarded { get; set; }
    public int ExpAwarded { get; set; }

    /// <summary>The title bracket whose XP width and Salary scaled the reward (DESIGN.md §3.3);
    /// null when no brackets existed.</summary>
    public int? TitleBracketId { get; set; }

    /// <summary>Personal x rank multiplier applied per currency at grant time — coins use the
    /// salary multipliers, gems and XP their GemBonus/ExpBonus ones (KNG-16).</summary>
    public decimal CoinMultiplier { get; set; } = 1.0m;
    public decimal GemMultiplier { get; set; } = 1.0m;
    public decimal ExpMultiplier { get; set; } = 1.0m;
}
