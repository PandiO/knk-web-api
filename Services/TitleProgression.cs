using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// The title-bracket side of an experience change, shared by every path that adds or removes XP
    /// (UserService.AdjustBalancesAsync, and siege match rewards - docs/specs/siege-minigame/DESIGN.md
    /// §7.6 "title brackets advance through the existing TitleService"). Pure: it mutates the given
    /// user only and saves nothing, so callers keep control of their own transaction/audit rules.
    /// </summary>
    public static class TitleProgression
    {
        /// <summary>
        /// Call after <paramref name="user"/>'s ExperiencePoints has been set to its new value.
        /// Resolves the bracket held at <paramref name="previousExperience"/> and, when the new XP
        /// lands in another bracket, reports the change. On a promotion every crossed bracket's
        /// Coin/Gem/Exp bonus is granted onto <paramref name="user"/> once (ExpBonus can push into a
        /// further bracket, which is then crossed too). Demotions never claw anything back.
        /// KNG-16: each bonus is scaled by the player's personal x rank multiplier for that currency -
        /// coins by the salary multipliers, gems and XP by their GemBonus/ExpBonus multipliers (no
        /// global multiplier); <paramref name="ranks"/> null = no active ranks.
        /// Returns null when there are no brackets or the bracket didn't change.
        /// </summary>
        public static TitleChangeResultDto? ApplyExperienceChange(User user, int previousExperience, List<TitleBracket>? brackets,
            RankMultipliersDto? ranks = null)
        {
            if (brackets == null || brackets.Count == 0) return null;

            var previousBracket = brackets.LastOrDefault(b => b.MinExperience <= previousExperience) ?? brackets[0];

            // Consolidate every bracket crossed by this single adjustment into one grant + one
            // reported change, instead of firing once per tier the way v1's TitleChangeEvents
            // loop did (setPromoteLoop/setDemoteLoop) — a developer-confirmed behavior NOT to
            // repeat. ExpBonus can itself push into a further bracket, so this loops until
            // resolution stabilizes, mirroring v1's cascading re-check but accumulating instead
            // of firing per-iteration effects.
            var direction = user.ExperiencePoints > previousExperience ? "promotion" : "demotion";
            var currentBracket = brackets.LastOrDefault(b => b.MinExperience <= user.ExperiencePoints) ?? brackets[0];

            if (currentBracket.Id == previousBracket.Id) return null;

            var crossed = new List<TitleBracket>();
            int coinBonusTotal = 0, gemBonusTotal = 0, expBonusTotal = 0;
            int coinBonusBase = 0, gemBonusBase = 0, expBonusBase = 0;
            var coinMultipliers = new List<RewardMultiplierDto>();
            var gemMultipliers = new List<RewardMultiplierDto>();
            var expMultipliers = new List<RewardMultiplierDto>();

            if (direction == "promotion")
            {
                ranks ??= RankMultipliersDto.Neutral;
                var coinMultiplier = user.PersonalSalaryMultiplier * ranks.Salary;
                var gemMultiplier = user.PersonalGemBonusMultiplier * ranks.GemBonus;
                var expMultiplier = user.PersonalExpBonusMultiplier * ranks.ExpBonus;
                coinMultipliers.Add(RewardMultiplierDto.Personal(user.PersonalSalaryMultiplier));
                coinMultipliers.AddRange(ranks.SalaryBreakdown());
                gemMultipliers.Add(RewardMultiplierDto.Personal(user.PersonalGemBonusMultiplier));
                gemMultipliers.AddRange(ranks.GemBonusBreakdown());
                expMultipliers.Add(RewardMultiplierDto.Personal(user.PersonalExpBonusMultiplier));
                expMultipliers.AddRange(ranks.ExpBonusBreakdown());

                // Walk every bracket strictly above previousBracket up to (and possibly
                // past, if ExpBonus pushes further) currentBracket, accumulating rewards.
                var idx = brackets.FindIndex(b => b.Id == previousBracket.Id) + 1;
                while (idx < brackets.Count && brackets[idx].MinExperience <= user.ExperiencePoints)
                {
                    var tier = brackets[idx];
                    crossed.Add(tier);
                    var expBonus = ScaleBonus(tier.ExpBonus, expMultiplier);
                    coinBonusTotal += ScaleBonus(tier.CoinBonus, coinMultiplier);
                    gemBonusTotal += ScaleBonus(tier.GemBonus, gemMultiplier);
                    expBonusTotal += expBonus;
                    coinBonusBase += tier.CoinBonus;
                    gemBonusBase += tier.GemBonus;
                    expBonusBase += tier.ExpBonus;
                    user.ExperiencePoints += expBonus; // may unlock further brackets
                    idx++;
                }
                user.Coins += coinBonusTotal;
                user.Gems += gemBonusTotal;
                currentBracket = brackets.LastOrDefault(b => b.MinExperience <= user.ExperiencePoints) ?? brackets[0];
            }
            // Demotion never claws back currency (matches v1's userDemotion, which only
            // ever removed structural slots/skills — neither exists in v3), so no bonus
            // accumulation happens on the way down.

            return new TitleChangeResultDto
            {
                Direction = direction,
                FromTitleBracketId = previousBracket.Id,
                FromTitleName = previousBracket.NameFor(user.Gender),
                ToTitleBracketId = currentBracket.Id,
                ToTitleName = currentBracket.NameFor(user.Gender),
                CrossedTitles = crossed.Select(t => new TitleCrossingDto { TitleBracketId = t.Id, TitleName = t.NameFor(user.Gender) }).ToList(),
                CoinBonusGranted = coinBonusTotal,
                GemBonusGranted = gemBonusTotal,
                ExpBonusGranted = expBonusTotal,
                CoinBonusBase = coinBonusBase,
                GemBonusBase = gemBonusBase,
                ExpBonusBase = expBonusBase,
                CoinBonusMultipliers = coinMultipliers,
                GemBonusMultipliers = gemMultipliers,
                ExpBonusMultipliers = expMultipliers
            };
        }

        /// <summary>
        /// Would raising XP from <paramref name="from"/> to <paramref name="to"/> cross into a higher
        /// bracket? Lets callers skip loading rank multipliers for XP gains that promote nobody.
        /// </summary>
        public static bool CrossesUp(List<TitleBracket>? brackets, int from, int to) =>
            brackets != null && to > from && brackets.Any(b => b.MinExperience > from && b.MinExperience <= to);

        /// <summary>A bonus times its multiplier, rounded half away from zero, never negative.</summary>
        public static int ScaleBonus(int bonus, decimal multiplier) =>
            Math.Max(0, (int)Math.Round(bonus * multiplier, MidpointRounding.AwayFromZero));
    }
}
