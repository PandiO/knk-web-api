using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// The title bracket resolved from a user's current ExperiencePoints
    /// (docs/specs/user-features/IMPLEMENTATION_PLAN.md §4). Null fields mean no title brackets
    /// are seeded yet.
    /// </summary>
    public class TitleResolutionDto
    {
        [JsonPropertyName("titleBracketId")]
        public int? TitleBracketId { get; set; }

        [JsonPropertyName("titleName")]
        public string? TitleName { get; set; }

        /// <summary>Base salary for the resolved bracket (0 if no brackets seeded).</summary>
        [JsonPropertyName("salary")]
        public int Salary { get; set; }

        /// <summary>
        /// XP earned past the highest bracket's threshold — a pure prestige signal once a user
        /// has reached the final title (DESIGN.md §3), 0 otherwise.
        /// </summary>
        [JsonPropertyName("prestigeExperience")]
        public int PrestigeExperience { get; set; }

        /// <summary>
        /// The next bracket above the current one, or null if the current bracket is already the
        /// highest seeded (a pure-prestige state — see PrestigeExperience). Backs the
        /// user-management admin module's "progress toward next bracket" view
        /// (docs/specs/user-management/DESIGN.md §2).
        /// </summary>
        [JsonPropertyName("nextTitleBracketId")]
        public int? NextTitleBracketId { get; set; }

        [JsonPropertyName("nextTitleName")]
        public string? NextTitleName { get; set; }

        [JsonPropertyName("nextTitleMinExperience")]
        public int? NextTitleMinExperience { get; set; }
    }

    /// <summary>
    /// One title bracket as listed by <c>GET /api/title-brackets</c> (InventoryMenu content port
    /// CP3: the in-game Profile menu's title list and the Player manager's title picker). Ordered
    /// by <see cref="MinExperience"/>; the bonus fields are the one-time rewards for first reaching
    /// the bracket.
    /// </summary>
    public class TitleBracketDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("maleName")]
        public string MaleName { get; set; } = null!;

        [JsonPropertyName("femaleName")]
        public string FemaleName { get; set; } = null!;

        [JsonPropertyName("minExperience")]
        public int MinExperience { get; set; }

        [JsonPropertyName("salary")]
        public int Salary { get; set; }

        [JsonPropertyName("coinBonus")]
        public int CoinBonus { get; set; }

        [JsonPropertyName("gemBonus")]
        public int GemBonus { get; set; }

        [JsonPropertyName("expBonus")]
        public int ExpBonus { get; set; }
    }

    /// <summary>One title bracket crossed during a consolidated promotion/demotion
    /// (UserService.AdjustBalancesAsync). See BalanceAdjustmentResultDto.</summary>
    public class TitleCrossingDto
    {
        [JsonPropertyName("titleBracketId")]
        public int TitleBracketId { get; set; }

        [JsonPropertyName("titleName")]
        public string TitleName { get; set; } = null!;
    }

    /// <summary>Result of UserService.AdjustBalancesAsync — the caller's requested balance
    /// change plus, if it crossed one or more title brackets, everything needed to show one
    /// consolidated promotion/demotion notification instead of one per tier (per the developer's
    /// explicit instruction, since v1's TitleChangeEvents looped once per tier crossed).</summary>
    public class BalanceAdjustmentResultDto
    {
        [JsonPropertyName("newCoins")]
        public int NewCoins { get; set; }

        [JsonPropertyName("newGems")]
        public int NewGems { get; set; }

        [JsonPropertyName("newExperiencePoints")]
        public int NewExperiencePoints { get; set; }

        /// <summary>Null if no title bracket was crossed by this adjustment.</summary>
        [JsonPropertyName("titleChange")]
        public TitleChangeResultDto? TitleChange { get; set; }
    }

    public class TitleChangeResultDto
    {
        /// <summary>"promotion" or "demotion".</summary>
        [JsonPropertyName("direction")]
        public string Direction { get; set; } = null!;

        [JsonPropertyName("fromTitleBracketId")]
        public int FromTitleBracketId { get; set; }

        [JsonPropertyName("fromTitleName")]
        public string FromTitleName { get; set; } = null!;

        [JsonPropertyName("toTitleBracketId")]
        public int ToTitleBracketId { get; set; }

        [JsonPropertyName("toTitleName")]
        public string ToTitleName { get; set; } = null!;

        /// <summary>Every bracket crossed, in order, including the final one — e.g. jumping
        /// Peasant -> Reeve lists Yeoman, Squire, Reeve. Promotion only; always empty on
        /// demotion (v1 never granted/showed per-tier detail on the way down beyond the
        /// from/to names, and demotion grants no currency — see the service's doc comment).</summary>
        [JsonPropertyName("crossedTitles")]
        public List<TitleCrossingDto> CrossedTitles { get; set; } = new();

        /// <summary>Summed CoinBonus/GemBonus/ExpBonus across every bracket crossed on
        /// promotion, after the player's multipliers (KNG-16: coins x personal/rank salary
        /// multipliers, gems and XP x their own GemBonus/ExpBonus multipliers). Always 0 on
        /// demotion (v1 never clawed back currency on demotion).</summary>
        [JsonPropertyName("coinBonusGranted")]
        public int CoinBonusGranted { get; set; }

        [JsonPropertyName("gemBonusGranted")]
        public int GemBonusGranted { get; set; }

        [JsonPropertyName("expBonusGranted")]
        public int ExpBonusGranted { get; set; }

        /// <summary>The crossed brackets' CoinBonus/GemBonus/ExpBonus summed before any
        /// multiplier (KNG-16), for the plugin's reward message. 0 on demotion.</summary>
        [JsonPropertyName("coinBonusBase")]
        public int CoinBonusBase { get; set; }

        [JsonPropertyName("gemBonusBase")]
        public int GemBonusBase { get; set; }

        [JsonPropertyName("expBonusBase")]
        public int ExpBonusBase { get; set; }

        /// <summary>The multipliers applied to each bonus: personal first, then one per active
        /// rank with its name and colors. Empty on demotion.</summary>
        [JsonPropertyName("coinBonusMultipliers")]
        public List<RewardMultiplierDto> CoinBonusMultipliers { get; set; } = new();

        [JsonPropertyName("gemBonusMultipliers")]
        public List<RewardMultiplierDto> GemBonusMultipliers { get; set; } = new();

        [JsonPropertyName("expBonusMultipliers")]
        public List<RewardMultiplierDto> ExpBonusMultipliers { get; set; } = new();
    }
}
