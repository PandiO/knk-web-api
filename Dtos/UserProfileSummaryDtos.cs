using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// Current salary state for the profile view (docs/specs/user-management/DESIGN.md §2) — the
    /// multiplier breakdown PayOutAsync would use right now, without triggering a payout.
    /// </summary>
    public class SalaryStateDto
    {
        /// <summary>Coins per hour of the user's current title bracket, before any multiplier.</summary>
        [JsonPropertyName("titleSalary")]
        public int TitleSalary { get; set; }

        [JsonPropertyName("globalMultiplier")]
        public decimal GlobalMultiplier { get; set; }

        [JsonPropertyName("personalMultiplier")]
        public decimal PersonalMultiplier { get; set; }

        [JsonPropertyName("rankMultiplier")]
        public decimal RankMultiplier { get; set; }

        /// <summary>Coins paid per hour right now (title salary x global x personal x rank).</summary>
        [JsonPropertyName("effectiveHourlyRate")]
        public decimal EffectiveHourlyRate { get; set; }

        [JsonPropertyName("lastSalaryPayoutAt")]
        public DateTime LastSalaryPayoutAt { get; set; }

        /// <summary>When SalaryService.PayOutAsync would next actually pay out (LastSalaryPayoutAt + 1 hour).</summary>
        [JsonPropertyName("nextEligibleAt")]
        public DateTime NextEligibleAt { get; set; }
    }

    /// <summary>
    /// Composite player-profile view (docs/specs/user-management/DESIGN.md §2,
    /// IMPLEMENTATION_PLAN.md Phase 1) — one aggregate response for the admin
    /// `/admin/users/:id` page, assembled from the same services their individual endpoints
    /// already use rather than a parallel read path.
    /// </summary>
    public class UserProfileSummaryDto
    {
        [JsonPropertyName("account")]
        public UserDto Account { get; set; } = null!;

        [JsonPropertyName("permissions")]
        public PermissionEffectiveResponseDto Permissions { get; set; } = null!;

        [JsonPropertyName("groups")]
        public List<UserPermissionGroupDto> Groups { get; set; } = new();

        [JsonPropertyName("title")]
        public TitleResolutionDto Title { get; set; } = null!;

        [JsonPropertyName("salary")]
        public SalaryStateDto Salary { get; set; } = null!;

        /// <summary>
        /// PandiO/knk-workspace#183 (S4): whether the player is online in game (<c>User.IsOnline</c>, as on the
        /// moderation list); null when it could not be read. Kept live by the web hub's <c>PresenceChanged</c>.
        /// </summary>
        [JsonPropertyName("presence")]
        public ProfilePresenceDto? Presence { get; set; }
    }

    /// <summary>PandiO/knk-workspace#183: the player profile's online badge.</summary>
    public class ProfilePresenceDto
    {
        [JsonPropertyName("online")]
        public bool Online { get; set; }

        [JsonPropertyName("lastSeenAt")]
        public DateTime? LastSeenAt { get; set; }
    }
}
