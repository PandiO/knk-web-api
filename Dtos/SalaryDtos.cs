using System;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// The global salary configuration (docs/specs/user-features/DESIGN.md §5). Singleton, admin-
    /// editable via the web app — mirrors GameSettingsController's read shape.
    /// </summary>
    public class SalaryConfigurationDto
    {
        /// <summary>
        /// Base coins paid per hour at a neutral (1.0) personal/rank multiplier. Despite the name
        /// (matching IMPLEMENTATION_PLAN.md §6's "global multiplier" language — one of three
        /// multipliers applied to hours-elapsed alongside personal/rank), this is the only one of
        /// the three with no other base rate to scale, so it doubles as the base hourly rate.
        /// Default 1.0 is a placeholder — tune to a real economy value via PUT before relying on it.
        /// </summary>
        [JsonPropertyName("globalMultiplier")]
        public decimal GlobalMultiplier { get; set; } = 1.0m;

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// DTO for updating the global salary configuration.
    /// </summary>
    public class UpdateSalaryConfigurationDto
    {
        [JsonPropertyName("globalMultiplier")]
        public decimal GlobalMultiplier { get; set; } = 1.0m;
    }

    /// <summary>
    /// Result of SalaryService.PayOutAsync (docs/specs/user-features/IMPLEMENTATION_PLAN.md §6).
    /// Always returned, whether or not a payout actually happened — Paid distinguishes the two.
    /// </summary>
    public class SalaryPayoutResultDto
    {
        /// <summary>False when less than an hour has passed since LastSalaryPayoutAt — no balance
        /// change was made, and every multiplier/coin field below is 0/unset.</summary>
        [JsonPropertyName("paid")]
        public bool Paid { get; set; }

        [JsonPropertyName("amountPaid")]
        public int AmountPaid { get; set; }

        [JsonPropertyName("hoursCovered")]
        public decimal HoursCovered { get; set; }

        [JsonPropertyName("globalMultiplier")]
        public decimal GlobalMultiplier { get; set; }

        [JsonPropertyName("personalMultiplier")]
        public decimal PersonalMultiplier { get; set; }

        /// <summary>Product of SalaryMultiplier across every currently-active (non-expired)
        /// PermissionGroup membership the user holds. 1.0 (neutral) if they hold none.</summary>
        [JsonPropertyName("rankMultiplier")]
        public decimal RankMultiplier { get; set; }

        [JsonPropertyName("newCoinsBalance")]
        public int NewCoinsBalance { get; set; }

        [JsonPropertyName("lastSalaryPayoutAt")]
        public DateTime LastSalaryPayoutAt { get; set; }

        /// <summary>When the next payout becomes eligible (LastSalaryPayoutAt + 1 hour) — set
        /// whether or not this call actually paid out.</summary>
        [JsonPropertyName("nextEligibleAt")]
        public DateTime NextEligibleAt { get; set; }
    }
}
