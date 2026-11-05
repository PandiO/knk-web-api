using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// The §7 scaling formulas of the survival design (knk-workspace docs/specs/siege-survival/DESIGN.md §7, S29,
    /// S30, S54, S55), the same maths as knk-core's ScalingCurve / WaveScaling, so the editor's per-round preview
    /// (§11) shows what the plugin will do. Round r starts at 1. Count: curve(r) × (1 + p·(players − 1)), floored,
    /// never below 1, capped by the count cap. Stats: the shared stat curve has base 1 (round 1 = vanilla) and each
    /// stat follows it with its weight w: 1 + w·(curve(r) − 1), × the per-player factor when it applies to stats,
    /// never below 0, and the stat cap last.
    /// </summary>
    public static class SurvivalScaling
    {
        /// <summary>The hard cap on the effective walking speed (blocks/tick attribute) above which pathfinding breaks.</summary>
        public const double WalkingSpeedHardCap = 0.5;
        /// <summary>The recommended effective walking speed limit (knk-core WalkingSpeedLimit, checks report §10).</summary>
        public const double WalkingSpeedRecommended = 0.4;

        public static double CurveValue(SurvivalScalingPreset preset, double b, double a, double m, int s, int round)
        {
            if (round < 1) throw new ArgumentOutOfRangeException(nameof(round), "round must be >= 1");
            return preset switch
            {
                SurvivalScalingPreset.Linear => b + a * (round - 1),
                SurvivalScalingPreset.Logarithmic => b + a * Math.Log(round),
                SurvivalScalingPreset.Exponential => b * Math.Pow(m, round - 1),
                SurvivalScalingPreset.Hybrid => round <= s
                    ? b + a * (round - 1)
                    : (b + a * (s - 1)) * Math.Pow(m, round - s),
                _ => throw new ArgumentOutOfRangeException(nameof(preset))
            };
        }

        public static double CountCurveValue(SurvivalDifficultyProfile p, int round) =>
            CurveValue(p.CountPreset, p.CountBase, p.CountStep, p.CountMultiplier, p.CountSwitchRound, round);

        /// <summary>The stat curve with b = 1 (S30).</summary>
        public static double StatCurveValue(SurvivalDifficultyProfile p, int round) =>
            CurveValue(p.StatPreset, 1, p.StatStep, p.StatMultiplier, p.StatSwitchRound, round);

        public static double PerPlayerMultiplier(double factor, int players) =>
            1 + factor * (Math.Max(1, players) - 1);

        /// <summary>Whether the curve's value grows with the rounds (knk-core WaveScalingSettings.grows).</summary>
        public static bool Grows(SurvivalScalingPreset preset, double step, double multiplier) => preset switch
        {
            SurvivalScalingPreset.Linear or SurvivalScalingPreset.Logarithmic => step > 0,
            SurvivalScalingPreset.Exponential => multiplier > 1,
            SurvivalScalingPreset.Hybrid => step > 0 || multiplier > 1,
            _ => false
        };

        public static int EnemyCount(SurvivalDifficultyProfile p, int round, int players)
        {
            var raw = CountCurveValue(p, round) * PerPlayerMultiplier(p.PerPlayerFactor, players);
            var count = Math.Max(1, (int)Math.Floor(raw));
            return p.CountCap > 0 ? Math.Min(count, p.CountCap) : count;
        }

        public static double StatMultiplier(SurvivalDifficultyProfile p, double weight, int round, int players)
        {
            var multiplier = 1 + weight * (StatCurveValue(p, round) - 1);
            if (p.PerPlayerAppliesToStats) multiplier *= PerPlayerMultiplier(p.PerPlayerFactor, players);
            multiplier = Math.Max(0, multiplier);
            return Math.Min(multiplier, p.StatCap);
        }

        /// <summary>
        /// The highest walking-speed multiplier the profile can reach over all rounds: the stat cap when the
        /// weighted curve grows (or per-player stat scaling applies with more than one player), else 1.
        /// </summary>
        public static double MaxWalkingSpeedMultiplier(SurvivalDifficultyProfile p, int maxPlayers)
        {
            var grows = p.WalkingSpeedWeight > 0 && Grows(p.StatPreset, p.StatStep, p.StatMultiplier);
            var perPlayer = p.PerPlayerAppliesToStats && p.PerPlayerFactor > 0 && maxPlayers > 1;
            if (grows) return p.StatCap;
            if (perPlayer) return Math.Min(p.StatCap, PerPlayerMultiplier(p.PerPlayerFactor, maxPlayers));
            return 1;
        }

        public static SurvivalPreviewDto Preview(SurvivalDifficultyProfile p, int fromRound, int toRound, int players)
        {
            if (fromRound < 1) throw new ArgumentException("fromRound must be at least 1.");
            if (toRound < fromRound) throw new ArgumentException("toRound must be at least fromRound.");
            if (toRound - fromRound >= 500) throw new ArgumentException("A preview covers at most 500 rounds.");
            if (players < 1) throw new ArgumentException("players must be at least 1.");

            var preview = new SurvivalPreviewDto { DifficultyProfileId = p.Id, Players = players };
            for (var round = fromRound; round <= toRound; round++)
            {
                preview.Rounds.Add(new SurvivalRoundPreviewDto
                {
                    Round = round,
                    EnemyCount = EnemyCount(p, round, players),
                    CurveValue = StatCurveValue(p, round),
                    Health = StatMultiplier(p, p.HealthWeight, round, players),
                    WalkingSpeed = StatMultiplier(p, p.WalkingSpeedWeight, round, players),
                    MeleeDamage = StatMultiplier(p, p.MeleeDamageWeight, round, players),
                    RangedDamage = StatMultiplier(p, p.RangedDamageWeight, round, players),
                    AttackSpeed = StatMultiplier(p, p.AttackSpeedWeight, round, players)
                });
            }
            return preview;
        }

        /// <summary>
        /// Vanilla movement speed per monster type, for the walking-speed readiness check (knk-core
        /// VanillaProfiles: zombie 0.23, skeleton/creeper 0.25, spider 0.3; unknown types = 0.25). A lookup, not a
        /// balance value.
        /// </summary>
        public static double VanillaWalkingSpeed(string entityType) => entityType.Trim().ToUpperInvariant() switch
        {
            "ZOMBIE" or "HUSK" or "DROWNED" or "ZOMBIE_VILLAGER" or "ZOMBIFIED_PIGLIN" => 0.23,
            "SPIDER" or "CAVE_SPIDER" => 0.3,
            "ENDERMAN" => 0.3,
            "WOLF" => 0.3,
            "VINDICATOR" or "PILLAGER" or "EVOKER" or "RAVAGER" => 0.35,
            _ => 0.25
        };

        /// <summary>Percent (0.01 % precision) to basis points; throws when finer than 0.01 % or outside 0..100.</summary>
        public static int ToBasisPoints(decimal percent)
        {
            if (percent < 0 || percent > 100) throw new ArgumentException("A chance must be between 0 and 100 %.");
            var scaled = percent * 100;
            if (scaled != decimal.Truncate(scaled)) throw new ArgumentException("A chance must be a multiple of 0.01 %.");
            return (int)scaled;
        }
    }
}
