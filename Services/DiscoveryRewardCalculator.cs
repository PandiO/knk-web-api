using System;
using System.Collections.Generic;
using System.Linq;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    /// <summary>A discovery reward in whole units: coins, gems, XP.</summary>
    public readonly record struct DiscoveryReward(int Coins, int Gems, int Exp);

    /// <summary>
    /// Domain discovery reward math (docs/specs/domain-discovery/DESIGN.md §3.3), pure so it can
    /// be tested without a database; randomness comes from the caller's <see cref="Random"/>.
    ///
    /// For a player whose XP puts them in title bracket b:
    /// <list type="bullet">
    /// <item>XP = round(uniform(ExpUnitsMin, ExpUnitsMax) x unit), unit = 1% of b's XP width
    /// (next bracket's MinExperience - b's) — exactly v1's User.getExpPart, so Town 1-4 units is
    /// v1's 1%-4% of the bracket. The top bracket (v3 has no MaxExp) uses the width of the bracket
    /// below it; no brackets at all uses Serf's unit, 25.</item>
    /// <item>Coins = round(uniform(CoinSalaryHoursMin, CoinSalaryHoursMax) x b.Salary) — hours of
    /// the title's salary (developer-approved Q1; v1 coins were flat).</item>
    /// <item>Gems = uniform whole number in [GemsMin, GemsMax], flat like v1.</item>
    /// </list>
    /// These are the base amounts; <see cref="knkwebapi_v2.Dtos.CurrencyMultipliersDto"/> scales
    /// them by the player's personal and rank multipliers afterwards.
    /// </summary>
    public static class DiscoveryRewardCalculator
    {
        /// <summary>Serf's XP unit (bracket width 2,500 / 100), used when no brackets exist.</summary>
        public const int FallbackExpUnit = 25;

        /// <summary>Serf's Salary, used when no brackets exist.</summary>
        public const int FallbackSalary = 650;

        /// <summary>The type rule with every non-null field of <paramref name="domainOverride"/>
        /// applied; a new instance, the inputs are left untouched.</summary>
        public static DiscoveryRewardRule Merge(DiscoveryRewardRule rule, DomainDiscoveryOverride? domainOverride)
        {
            var o = domainOverride;
            return new DiscoveryRewardRule
            {
                DomainType = rule.DomainType,
                IsEnabled = o?.IsEnabled ?? rule.IsEnabled,
                ExpUnitsMin = o?.ExpUnitsMin ?? rule.ExpUnitsMin,
                ExpUnitsMax = o?.ExpUnitsMax ?? rule.ExpUnitsMax,
                CoinSalaryHoursMin = o?.CoinSalaryHoursMin ?? rule.CoinSalaryHoursMin,
                CoinSalaryHoursMax = o?.CoinSalaryHoursMax ?? rule.CoinSalaryHoursMax,
                GemsMin = o?.GemsMin ?? rule.GemsMin,
                GemsMax = o?.GemsMax ?? rule.GemsMax,
                IncludeAncestors = o?.IncludeAncestors ?? rule.IncludeAncestors,
                UpdatedAt = o != null && o.UpdatedAt > rule.UpdatedAt ? o.UpdatedAt : rule.UpdatedAt
            };
        }

        /// <summary>The bracket a player with <paramref name="experiencePoints"/> holds: the highest
        /// whose MinExperience is at or below it (lowest if below all, like TitleService). Null
        /// when there are no brackets.</summary>
        public static TitleBracket? CurrentBracket(IReadOnlyList<TitleBracket> ordered, int experiencePoints) =>
            ordered.LastOrDefault(b => b.MinExperience <= experiencePoints) ?? ordered.FirstOrDefault();

        /// <summary>1% of <paramref name="bracket"/>'s XP width, rounded down (v1 int division).</summary>
        public static int ExpUnit(IReadOnlyList<TitleBracket> ordered, TitleBracket? bracket)
        {
            if (bracket == null) return FallbackExpUnit;
            var index = IndexOf(ordered, bracket);
            if (index < 0) return FallbackExpUnit;

            if (index + 1 < ordered.Count)
            {
                return Math.Max(0, (ordered[index + 1].MinExperience - bracket.MinExperience) / 100);
            }
            // Top bracket: v1 used (MaxExp - MinExp) / 1000, but v3 has no MaxExp — use the width
            // of the bracket below instead (DESIGN.md D4).
            return index > 0
                ? Math.Max(0, (bracket.MinExperience - ordered[index - 1].MinExperience) / 100)
                : FallbackExpUnit;
        }

        /// <summary>The Salary coins are counted in hours of.</summary>
        public static int SalaryOf(TitleBracket? bracket) => bracket?.Salary ?? FallbackSalary;

        /// <summary>One random base reward for <paramref name="rule"/>.</summary>
        public static DiscoveryReward Roll(DiscoveryRewardRule rule, int expUnit, int salary, Random random)
        {
            var (expLo, expHi) = Range(rule.ExpUnitsMin, rule.ExpUnitsMax);
            var (coinLo, coinHi) = Range(rule.CoinSalaryHoursMin, rule.CoinSalaryHoursMax);
            var (gemLo, gemHi) = Range(rule.GemsMin, rule.GemsMax);

            var expUnits = expLo + (expHi - expLo) * (decimal)random.NextDouble();
            var coinHours = coinLo + (coinHi - coinLo) * (decimal)random.NextDouble();
            var gems = gemHi == int.MaxValue ? random.Next(gemLo, gemHi) : random.Next(gemLo, gemHi + 1);

            return new DiscoveryReward(ToWhole(coinHours * salary), gems, ToWhole(expUnits * expUnit));
        }

        /// <summary>The smallest base reward <paramref name="rule"/> can roll.</summary>
        public static DiscoveryReward Min(DiscoveryRewardRule rule, int expUnit, int salary) => new(
            ToWhole(Range(rule.CoinSalaryHoursMin, rule.CoinSalaryHoursMax).Lo * salary),
            Range(rule.GemsMin, rule.GemsMax).Lo,
            ToWhole(Range(rule.ExpUnitsMin, rule.ExpUnitsMax).Lo * expUnit));

        /// <summary>The largest base reward <paramref name="rule"/> can roll.</summary>
        public static DiscoveryReward Max(DiscoveryRewardRule rule, int expUnit, int salary) => new(
            ToWhole(Range(rule.CoinSalaryHoursMin, rule.CoinSalaryHoursMax).Hi * salary),
            Range(rule.GemsMin, rule.GemsMax).Hi,
            ToWhole(Range(rule.ExpUnitsMin, rule.ExpUnitsMax).Hi * expUnit));

        /// <summary>Rounded away from zero, never negative, saturating at int.MaxValue.</summary>
        public static int ToWhole(decimal value)
        {
            var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
            if (rounded <= 0) return 0;
            return rounded >= int.MaxValue ? int.MaxValue : (int)rounded;
        }

        // Validation keeps min <= max and nothing negative, but a direct DB edit could break
        // either, so order the pair and floor it at 0 rather than roll nonsense.
        private static (decimal Lo, decimal Hi) Range(decimal a, decimal b) => (Math.Max(0, Math.Min(a, b)), Math.Max(0, Math.Max(a, b)));

        private static (int Lo, int Hi) Range(int a, int b) => (Math.Max(0, Math.Min(a, b)), Math.Max(0, Math.Max(a, b)));

        private static int IndexOf(IReadOnlyList<TitleBracket> ordered, TitleBracket bracket)
        {
            for (var i = 0; i < ordered.Count; i++)
            {
                if (ReferenceEquals(ordered[i], bracket) || ordered[i].Id == bracket.Id) return i;
            }
            return -1;
        }
    }
}
