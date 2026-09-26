using System;
using System.Collections.Generic;
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
        /// Server-wide multiplier on every salary payout, applied alongside the personal and rank
        /// multipliers to the base hourly rate — the Salary of the user's current title bracket
        /// (TitleBracket.Salary). 1.0 (the default) pays titles' Salary as-is.
        /// </summary>
        [JsonPropertyName("globalMultiplier")]
        public decimal GlobalMultiplier { get; set; } = 1.0m;

        /// <summary>
        /// Hours of a gap between payouts that count toward salary. Hour N of a gap pays 1/N of
        /// an hour (the first in full), and hours past this limit pay nothing. Default 720 (30
        /// days); 1 pays a single hour however long the player was away.
        /// </summary>
        [JsonPropertyName("offlinePayoutMaxHours")]
        public int OfflinePayoutMaxHours { get; set; } = 720;

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

        /// <summary>Omitted (null) keeps the current value.</summary>
        [JsonPropertyName("offlinePayoutMaxHours")]
        public int? OfflinePayoutMaxHours { get; set; }
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

        /// <summary>Real time since the last payout.</summary>
        [JsonPropertyName("hoursCovered")]
        public decimal HoursCovered { get; set; }

        /// <summary>Hours of salary HoursCovered was worth after log decay (see
        /// SalaryService.PaidHoursFor) — about 1 for an hourly online payout, at most ~7.2 for a
        /// gap of 30 days or more.</summary>
        [JsonPropertyName("paidHours")]
        public decimal PaidHours { get; set; }

        /// <summary>The title bracket whose Salary was used as the hourly base rate.</summary>
        [JsonPropertyName("titleBracketId")]
        public int? TitleBracketId { get; set; }

        /// <summary>Coins per hour of the user's title bracket, before any multiplier.</summary>
        [JsonPropertyName("titleSalary")]
        public int TitleSalary { get; set; }

        [JsonPropertyName("globalMultiplier")]
        public decimal GlobalMultiplier { get; set; }

        [JsonPropertyName("personalMultiplier")]
        public decimal PersonalMultiplier { get; set; }

        /// <summary>Product of SalaryMultiplier across every currently-active (non-expired)
        /// PermissionGroup membership the user holds. 1.0 (neutral) if they hold none.</summary>
        [JsonPropertyName("rankMultiplier")]
        public decimal RankMultiplier { get; set; }

        /// <summary>Title salary x paid hours: the amount before any multiplier.</summary>
        [JsonPropertyName("baseAmount")]
        public decimal BaseAmount { get; set; }

        /// <summary>Every multiplier applied to BaseAmount (global, personal, then one per active
        /// rank with its name and colors), for the plugin's payout message. Their product is
        /// GlobalMultiplier x PersonalMultiplier x RankMultiplier.</summary>
        [JsonPropertyName("multipliers")]
        public List<RewardMultiplierDto> Multipliers { get; set; } = new();

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
