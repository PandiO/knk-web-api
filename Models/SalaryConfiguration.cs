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
    /// Base coins paid per hour at a neutral (1.0) personal/rank multiplier — see
    /// SalaryConfigurationDto's doc comment for why this one field also functions as the base
    /// hourly rate. Default 1.0 is a placeholder; tune via PUT before relying on real payouts.
    /// </summary>
    public decimal GlobalMultiplier { get; set; } = 1.0m;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
