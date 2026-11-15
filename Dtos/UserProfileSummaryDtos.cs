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
        /// <summary>The account (<see cref="UserDto"/>) plus who froze the player and when (#565).</summary>
        [JsonPropertyName("account")]
        public ProfileAccountDto Account { get; set; } = null!;

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

    /// <summary>
    /// PandiO/knk-workspace#565: the profile's account, i.e. <see cref="UserDto"/> plus the freeze details only staff
    /// see (when and by whom). <c>isFrozen</c> and <c>frozenReason</c> come from <see cref="UserDto"/>; the three
    /// fields here are always written (null while not frozen), so a client can tell this API from an older one.
    /// </summary>
    public class ProfileAccountDto : UserDto
    {
        /// <summary>When the current freeze was set (UTC); null while not frozen.</summary>
        [JsonPropertyName("frozenAt")]
        public DateTime? FrozenAt { get; set; }

        /// <summary>The staff member who set the current freeze; null while not frozen or when the game server froze
        /// the player without an acting user.</summary>
        [JsonPropertyName("frozenByUserId")]
        public int? FrozenByUserId { get; set; }

        /// <summary>That staff member's username now (null when unknown or deleted).</summary>
        [JsonPropertyName("frozenByUsername")]
        public string? FrozenByUsername { get; set; }

        /// <summary>A copy of <paramref name="account"/> with every public read/write property of <see cref="UserDto"/>.</summary>
        public static ProfileAccountDto From(UserDto account)
        {
            var copy = new ProfileAccountDto();
            foreach (var property in typeof(UserDto).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length > 0 || property.GetGetMethod() == null || property.GetSetMethod() == null)
                {
                    continue;
                }
                property.SetValue(copy, property.GetValue(account));
            }
            return copy;
        }
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
