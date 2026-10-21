using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services.Duel
{
    /// <summary>What the review of one decisive value-moving result looks at (all read by DuelMatchService).</summary>
    public sealed record DuelReviewInput
    {
        public required DuelPolicy Policy { get; init; }
        public required DuelGearMode GearMode { get; init; }
        public DuelTelemetryDto? Telemetry { get; init; }

        /// <summary>Which side won, to read the loser's and the winner's counters.</summary>
        public required bool WinnerIsChallenger { get; init; }

        public required int WinnerUserId { get; init; }

        /// <summary>The winners of the pair's earlier decisive value-moving duels, most recent first.</summary>
        public IReadOnlyList<int> PriorPairWinners { get; init; } = Array.Empty<int>();

        /// <summary>Different opponents with young accounts the winner won value from within S6WindowDays,
        /// this match's loser included when young.</summary>
        public int YoungOpponents { get; init; }

        /// <summary>Earlier flags that still count (DuelMatchService.CountingFlags) of this pair, either direction.</summary>
        public int PriorPairFlags { get; init; }

        /// <summary>Earlier flags that still count where this winner was the winner.</summary>
        public int PriorBeneficiaryFlags { get; init; }
    }

    /// <summary>The tier (0 clean, 1 flagged, 2 held), the signals that fired, and the numbers behind them.</summary>
    public sealed record DuelReviewOutcome(int Tier, IReadOnlyList<string> Signals, bool EscalatedByPair,
        bool EscalatedByBeneficiary, IReadOnlyDictionary<string, object?> Details);

    /// <summary>
    /// Signals S1–S6 and the escalation tiers of arena duels DESIGN.md §10.3 (KNG-213). Pure: everything comes
    /// in through <see cref="DuelReviewInput"/>, so each rule is unit-tested without a database.
    /// <list type="bullet">
    /// <item>Any signal makes the result a flag (tier 1: paid and flagged).</item>
    /// <item>Tier 2 (held) when the same pair, or the same beneficiary, reaches EscalationFlagCount counting
    /// flags within the window, this one included.</item>
    /// <item><b>A first flag never holds</b> (developer, 2026-10-20): tier 2 needs at least one earlier counting
    /// flag, and the threshold is never below 2, whatever the policy says.</item>
    /// </list>
    /// </summary>
    public static class DuelReviewRules
    {
        public const string S1 = "S1";
        public const string S2 = "S2";
        public const string S3 = "S3";
        public const string S4 = "S4";
        public const string S5 = "S5";
        public const string S6 = "S6";

        public static DuelReviewOutcome Evaluate(DuelReviewInput input)
        {
            var policy = input.Policy;
            var signals = new List<string>();
            var details = new Dictionary<string, object?>();
            var telemetry = input.Telemetry;
            var winner = telemetry == null ? null : input.WinnerIsChallenger ? telemetry.Challenger : telemetry.Opponent;
            var loser = telemetry == null ? null : input.WinnerIsChallenger ? telemetry.Opponent : telemetry.Challenger;

            if (policy.S1Enabled && winner != null && loser != null && winner.DamageDealt > 0)
            {
                var percent = loser.DamageDealt * 100.0 / winner.DamageDealt;
                details["loserDamagePercent"] = Math.Round(percent, 1);
                if (percent < policy.S1LoserDamagePercent) signals.Add(S1);
            }
            if (policy.S2Enabled && telemetry != null)
            {
                details["durationSeconds"] = Math.Round(telemetry.DurationSeconds, 1);
                if (telemetry.DurationSeconds < policy.S2ShortFightSeconds) signals.Add(S2);
            }
            if (policy.S3Enabled && loser != null && input.GearMode is DuelGearMode.OWN_GEAR_KEEP or DuelGearMode.OWN_GEAR_DROP
                && loser.HadWeapon == false && loser.ArmorPoints == 0)
            {
                details["loserUnarmed"] = true;
                signals.Add(S3);
            }
            if (policy.S4Enabled && loser != null)
            {
                details["loserAttackAttempts"] = loser.AttackAttempts;
                if (loser.AttackAttempts <= policy.S4MaxLoserAttackAttempts) signals.Add(S4);
            }
            if (policy.S5Enabled && policy.S5PairDuels >= 2)
            {
                var needed = policy.S5PairDuels - 1;
                var recent = input.PriorPairWinners.Take(needed).ToList();
                details["pairWinStreak"] = 1 + recent.TakeWhile(w => w == input.WinnerUserId).Count();
                if (recent.Count == needed && recent.All(w => w == input.WinnerUserId)) signals.Add(S5);
            }
            if (policy.S6Enabled && policy.S6MinYoungOpponents >= 1)
            {
                details["youngOpponents"] = input.YoungOpponents;
                if (input.YoungOpponents >= policy.S6MinYoungOpponents) signals.Add(S6);
            }

            var threshold = Math.Max(2, policy.EscalationFlagCount);
            var flagged = signals.Count > 0;
            var byPair = flagged && input.PriorPairFlags >= 1 && input.PriorPairFlags + 1 >= threshold;
            var byBeneficiary = flagged && input.PriorBeneficiaryFlags >= 1 && input.PriorBeneficiaryFlags + 1 >= threshold;
            details["priorPairFlags"] = input.PriorPairFlags;
            details["priorBeneficiaryFlags"] = input.PriorBeneficiaryFlags;
            details["escalationThreshold"] = threshold;

            var tier = !flagged ? 0 : byPair || byBeneficiary ? 2 : 1;
            return new DuelReviewOutcome(tier, signals, byPair, byBeneficiary, details);
        }
    }
}
