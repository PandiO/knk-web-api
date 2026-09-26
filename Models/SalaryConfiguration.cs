using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// Singleton configuration entity for the salary system's global multiplier
/// (docs/specs/user-features/DESIGN.md §5). Mirrors GameSettings' singleton pattern (fixed
/// "global" Id).
/// </summary>
public class SalaryConfiguration
{
    public string Id { get; set; } = "global";

    /// <summary>
    /// Server-wide multiplier on the title-based hourly salary (TitleBracket.Salary), applied
    /// alongside the personal and rank multipliers. 1.0 (the default) pays titles' Salary as-is.
    /// </summary>
    public decimal GlobalMultiplier { get; set; } = 1.0m;

    /// <summary>
    /// How many hours of a gap between payouts count toward salary (log decay, see
    /// SalaryService.PaidHoursFor). Default 720 = 30 days; 1 pays a single hour however long the
    /// player was away.
    /// </summary>
    public int OfflinePayoutMaxHours { get; set; } = 720;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
