using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// One multiplier applied to a reward (salary payout or title promotion bonus, KNG-16), so the
    /// plugin can tell the player what was multiplied and why: "975 x2 Personal x1.2 Royal".
    /// </summary>
    public class RewardMultiplierDto
    {
        public const string SourceGlobal = "global";
        public const string SourcePersonal = "personal";
        public const string SourceRank = "rank";

        /// <summary>"global" (SalaryConfiguration), "personal" (User) or "rank" (PermissionGroup).</summary>
        [JsonPropertyName("source")]
        public string Source { get; set; } = SourcePersonal;

        [JsonPropertyName("value")]
        public decimal Value { get; set; } = 1.0m;

        /// <summary>Rank only: the group this multiplier comes from, with its display colors
        /// (KNG-7 "&amp;" codes) so the plugin can show the name the way chat does.</summary>
        [JsonPropertyName("permissionGroupId")]
        public int? PermissionGroupId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("isPremiumTier")]
        public bool IsPremiumTier { get; set; }

        [JsonPropertyName("chatPrimaryColor")]
        public string? ChatPrimaryColor { get; set; }

        [JsonPropertyName("chatSecondaryColor")]
        public string? ChatSecondaryColor { get; set; }

        public static RewardMultiplierDto Global(decimal value) => new() { Source = SourceGlobal, Value = value };

        public static RewardMultiplierDto Personal(decimal value) => new() { Source = SourcePersonal, Value = value };
    }

    /// <summary>One currently-active rank (PermissionGroup membership) and its reward multipliers.</summary>
    public class ActiveRankDto
    {
        public int PermissionGroupId { get; set; }
        public string Name { get; set; } = "";
        public bool IsPremiumTier { get; set; }
        public string? ChatPrimaryColor { get; set; }
        public string? ChatSecondaryColor { get; set; }
        public decimal SalaryMultiplier { get; set; } = 1.0m;
        public decimal GemBonusMultiplier { get; set; } = 1.0m;
        public decimal ExpBonusMultiplier { get; set; } = 1.0m;

        public static ActiveRankDto From(PermissionGroup group) => new()
        {
            PermissionGroupId = group.Id,
            Name = group.Name,
            IsPremiumTier = group.IsPremiumTier,
            ChatPrimaryColor = group.ChatPrimaryColor,
            ChatSecondaryColor = group.ChatSecondaryColor,
            SalaryMultiplier = group.SalaryMultiplier,
            GemBonusMultiplier = group.GemBonusMultiplier,
            ExpBonusMultiplier = group.ExpBonusMultiplier
        };

        public RewardMultiplierDto ToRewardMultiplier(decimal value) => new()
        {
            Source = RewardMultiplierDto.SourceRank,
            Value = value,
            PermissionGroupId = PermissionGroupId,
            Name = Name,
            IsPremiumTier = IsPremiumTier,
            ChatPrimaryColor = ChatPrimaryColor,
            ChatSecondaryColor = ChatSecondaryColor
        };
    }

    /// <summary>
    /// A user's currently-active ranks and the product of each rank multiplier across them (1.0
    /// each when they hold none; developer-confirmed rule: ranks stack multiplicatively). Salary
    /// scales salary and title coin bonuses; GemBonus and ExpBonus scale title gem/XP bonuses.
    /// </summary>
    public class RankMultipliersDto
    {
        public static RankMultipliersDto Neutral => new();

        public List<ActiveRankDto> Ranks { get; set; } = new();

        public decimal Salary => Ranks.Aggregate(1.0m, (product, r) => product * r.SalaryMultiplier);
        public decimal GemBonus => Ranks.Aggregate(1.0m, (product, r) => product * r.GemBonusMultiplier);
        public decimal ExpBonus => Ranks.Aggregate(1.0m, (product, r) => product * r.ExpBonusMultiplier);

        public List<RewardMultiplierDto> SalaryBreakdown() => Ranks.Select(r => r.ToRewardMultiplier(r.SalaryMultiplier)).ToList();
        public List<RewardMultiplierDto> GemBonusBreakdown() => Ranks.Select(r => r.ToRewardMultiplier(r.GemBonusMultiplier)).ToList();
        public List<RewardMultiplierDto> ExpBonusBreakdown() => Ranks.Select(r => r.ToRewardMultiplier(r.ExpBonusMultiplier)).ToList();

        /// <summary>The ranks among <paramref name="memberships"/> that are active at
        /// <paramref name="asOf"/> (no expiry, or one still in the future).</summary>
        public static RankMultipliersDto FromMemberships(IEnumerable<UserPermissionGroup> memberships, DateTime asOf) => new()
        {
            Ranks = memberships
                .Where(m => m.PermissionGroup != null && (m.ExpiresAt == null || m.ExpiresAt > asOf))
                .Select(m => ActiveRankDto.From(m.PermissionGroup!))
                .ToList()
        };
    }
}
