using System.Text.Json.Serialization;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Dtos
{
    // Siege shop L4 (knk-workspace docs/specs/siege-shop/DESIGN.md § 4.2, § 5.6, § 6.2.3-6.2.4; P08, P09, P20, W32;
    // PandiO/knk-workspace#869, #795): a player buys a siege weapon type's permanent unlock with coins, from the
    // plugin's Siege unlocks menu or their own web app profile.

    /// <summary>
    /// <c>POST api/SiegeWeaponUnlocks/purchase</c>. <see cref="UserId"/> is the player the game server buys for (plugin
    /// key, required then); a signed-in web user buys for themselves and leaves it out (or sends their own id).
    /// </summary>
    public class SiegeWeaponUnlockPurchaseRequestDto
    {
        [JsonPropertyName("templateId")] public int TemplateId { get; set; }
        [JsonPropertyName("userId")] public int? UserId { get; set; }
    }

    public class SiegeWeaponUnlockPurchaseResultDto
    {
        [JsonPropertyName("templateId")] public int TemplateId { get; set; }
        [JsonPropertyName("templateName")] public string TemplateName { get; set; } = string.Empty;
        [JsonPropertyName("userId")] public int UserId { get; set; }
        /// <summary>Coins charged by this call: 0 when the type was unlocked already (a double click) or is free.</summary>
        [JsonPropertyName("coinsPaid")] public int CoinsPaid { get; set; }
        /// <summary>True when the player had the unlock before this call; nothing was charged.</summary>
        [JsonPropertyName("alreadyUnlocked")] public bool AlreadyUnlocked { get; set; }
        [JsonPropertyName("unlockedAt")] public DateTime UnlockedAt { get; set; }
        /// <summary>The player's coins after the call.</summary>
        [JsonPropertyName("coins")] public int Coins { get; set; }
    }

    /// <summary>The state of one template for one player, in the order the menu and the profile panel check it.</summary>
    public static class SiegeWeaponUnlockStates
    {
        public const string Unlocked = "Unlocked";
        public const string NeedsTitle = "NeedsTitle";
        public const string NeedsCoins = "NeedsCoins";
        public const string Unlockable = "Unlockable";
    }

    public class SiegeWeaponUnlockOfferDto
    {
        [JsonPropertyName("templateId")] public int TemplateId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("kind")] public SiegeWeaponKind Kind { get; set; }
        [JsonPropertyName("unlockPrice")] public int UnlockPrice { get; set; }
        [JsonPropertyName("minTitleLevel")] public int? MinTitleLevel { get; set; }
        /// <summary>The minimum title's name for the player's gender, or null without a minimum.</summary>
        [JsonPropertyName("minTitleName")] public string? MinTitleName { get; set; }
        [JsonPropertyName("unlocked")] public bool Unlocked { get; set; }
        [JsonPropertyName("unlockedAt")] public DateTime? UnlockedAt { get; set; }
        [JsonPropertyName("meetsTitle")] public bool MeetsTitle { get; set; }
        [JsonPropertyName("canAfford")] public bool CanAfford { get; set; }
        /// <summary>One of <see cref="SiegeWeaponUnlockStates"/>.</summary>
        [JsonPropertyName("state")] public string State { get; set; } = SiegeWeaponUnlockStates.Unlockable;
    }

    /// <summary><c>GET api/SiegeWeaponUnlocks/mine</c> and <c>by-user/{userId}</c>: every template with the player's state.</summary>
    public class SiegeWeaponUnlocksOverviewDto
    {
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("coins")] public int Coins { get; set; }
        [JsonPropertyName("titleLevel")] public int? TitleLevel { get; set; }
        [JsonPropertyName("titleName")] public string? TitleName { get; set; }
        [JsonPropertyName("offers")] public List<SiegeWeaponUnlockOfferDto> Offers { get; set; } = new();
    }
}
