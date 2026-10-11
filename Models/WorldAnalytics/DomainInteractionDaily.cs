using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// Region entries, exits and discoveries of one domain on one local day (KNG-34 link 7,
/// IMPLEMENTATION_PLAN.md §1.4, DESIGN.md D11). Anonymous: <see cref="UniquePlayers"/> is a count
/// the plugin keeps in memory for the day, never a list of players.
/// </summary>
public class DomainInteractionDaily
{
    public DateOnly Day { get; set; }

    /// <summary>Domain id (no FK: a deleted domain keeps its history).</summary>
    public int DomainId { get; set; }

    /// <summary><c>enter</c>, <c>leave</c> or <c>discover</c>.</summary>
    public string Kind { get; set; } = null!;

    public int Count { get; set; }

    /// <summary>
    /// Distinct players of the day as reported by the busiest report (max, not sum: the plugin sends
    /// the running count of the day, so a re-sent window never inflates it).
    /// </summary>
    public int UniquePlayers { get; set; }
}
