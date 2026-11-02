using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Siege match rewards (docs/specs/siege-minigame/DESIGN.md §7.6), pure so the reward matrix is
    /// unit-testable and a repeat "complete" call can rebuild the same breakdown from the stored rows.
    /// For each participant still in the match at the end:
    /// <list type="bullet">
    /// <item>Win: CoinRewardWin / ExpRewardWin / GemRewardWin when their team's alliance won.</item>
    /// <item>Holding: per objective their team holds at the end and did not hold at the start,
    /// CoinRewardHolding / ExpRewardHolding (any team, not only attackers - fixes v2's N4).</item>
    /// <item>Capture: per objective they personally captured, at most once per objective,
    /// CoinRewardCapture / ExpRewardCapture (recapture ping-pong can't farm rewards).</item>
    /// <item>Gate damage (PandiO/knk-workspace#165): per full GateDamageRewardStep HP they took off the scenario's
    /// gates, CoinRewardGateDamage / ExpRewardGateDamage (a step below 1 pays nothing).</item>
    /// <item>Participation (PandiO/knk-workspace#166): CoinRewardParticipation / ExpRewardParticipation when they
    /// were active (not AFK) for at least ParticipationMinActivePercent of their time in the match, as the plugin
    /// reported it. Without a reported match time (0) only a 0 % requirement pays.</item>
    /// </list>
    /// Participants who left before the end get nothing. Negative configured amounts count as 0.
    /// </summary>
    public static class SiegeRewardCalculator
    {
        /// <param name="GateDamage">Gate HP the participant took off the scenario's gates (#165).</param>
        /// <param name="ActiveSeconds">Their active (not AFK) seconds in the match (#166).</param>
        /// <param name="MatchSeconds">Their time in the match: first join to the end, rejoins included (#166).</param>
        public sealed record ParticipantInput(int UserId, int? TeamId, bool PresentAtEnd,
            int GateDamage = 0, int ActiveSeconds = 0, int MatchSeconds = 0);

        /// <param name="CapturerUserIds">Everyone who captured this objective during the match.</param>
        public sealed record ObjectiveOutcome(int ObjectiveId, int? InitialHolderTeamId, int? FinalHolderTeamId, IReadOnlySet<int> CapturerUserIds);

        public sealed record ObjectiveEntry(int? ObjectiveId, int? FinalHolderTeamId, int? CapturedByUserId);

        public sealed record Reward(
            int UserId,
            int? TeamId,
            bool PresentAtEnd,
            bool Won,
            int HoldingCount,
            int CaptureCount,
            int Coins,
            int Experience,
            int Gems,
            int GateDamageSteps = 0,
            bool Participation = false);

        /// <summary>
        /// Every objective of the scenario with its start and end holder. The start holder is the
        /// objective's InitialHolderTeamId, else the scenario's first Defender (DESIGN §7.1, the same
        /// default runtime-config gives the plugin). The end holder is the last entry reported for the
        /// objective (entries are in capture order), else the start holder. Entries for objectives
        /// that are not in the scenario are ignored.
        /// </summary>
        public static List<ObjectiveOutcome> Outcomes(SiegeScenario scenario, IEnumerable<ObjectiveEntry> entries)
        {
            var firstDefenderId = SiegeTeamIdentity.FirstDefender(scenario.Teams)?.Id;
            var byObjective = entries
                .Where(e => e.ObjectiveId.HasValue)
                .GroupBy(e => e.ObjectiveId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var outcomes = new List<ObjectiveOutcome>();
            foreach (var objective in scenario.Objectives.OrderBy(o => o.SortOrder).ThenBy(o => o.Id))
            {
                var initial = objective.InitialHolderTeamId ?? firstDefenderId;
                var final = initial;
                var capturers = new HashSet<int>();
                if (byObjective.TryGetValue(objective.Id, out var list))
                {
                    foreach (var entry in list)
                    {
                        if (entry.FinalHolderTeamId.HasValue) final = entry.FinalHolderTeamId;
                        if (entry.CapturedByUserId.HasValue) capturers.Add(entry.CapturedByUserId.Value);
                    }
                }
                outcomes.Add(new ObjectiveOutcome(objective.Id, initial, final, capturers));
            }
            return outcomes;
        }

        public static Reward For(
            ParticipantInput participant,
            SiegeScenario scenario,
            int? winningAllianceGroup,
            IReadOnlyList<ObjectiveOutcome> outcomes)
        {
            if (!participant.PresentAtEnd)
            {
                return new Reward(participant.UserId, participant.TeamId, false, false, 0, 0, 0, 0, 0);
            }

            var team = participant.TeamId.HasValue
                ? scenario.Teams.FirstOrDefault(t => t.Id == participant.TeamId.Value)
                : null;

            var won = team != null && winningAllianceGroup.HasValue && team.AllianceGroup == winningAllianceGroup.Value;

            var holding = team == null
                ? 0
                : outcomes.Count(o => o.FinalHolderTeamId == team.Id && o.InitialHolderTeamId != team.Id);

            var captures = outcomes.Count(o => o.CapturerUserIds.Contains(participant.UserId));

            var coins = (won ? NonNegative(scenario.CoinRewardWin) : 0)
                + holding * NonNegative(scenario.CoinRewardHolding)
                + captures * NonNegative(scenario.CoinRewardCapture);
            var experience = (won ? NonNegative(scenario.ExpRewardWin) : 0)
                + holding * NonNegative(scenario.ExpRewardHolding)
                + captures * NonNegative(scenario.ExpRewardCapture);
            var gems = won ? NonNegative(scenario.GemRewardWin) : 0;

            var gateDamageSteps = GateDamageSteps(participant.GateDamage, scenario.GateDamageRewardStep);
            coins += gateDamageSteps * NonNegative(scenario.CoinRewardGateDamage);
            experience += gateDamageSteps * NonNegative(scenario.ExpRewardGateDamage);

            var participation = IsActiveEnough(participant.ActiveSeconds, participant.MatchSeconds, scenario.ParticipationMinActivePercent);
            if (participation)
            {
                coins += NonNegative(scenario.CoinRewardParticipation);
                experience += NonNegative(scenario.ExpRewardParticipation);
            }

            return new Reward(participant.UserId, participant.TeamId, true, won, holding, captures, coins, experience, gems,
                gateDamageSteps, participation);
        }

        /// <summary>Full reward steps in <paramref name="gateDamage"/> HP; 0 when the step is below 1.</summary>
        public static int GateDamageSteps(int gateDamage, int step) =>
            step < 1 || gateDamage <= 0 ? 0 : gateDamage / step;

        /// <summary>
        /// The participation rule (#166): active seconds at least <paramref name="minActivePercent"/> (clamped to
        /// 0-100) of the match seconds. A 0 % requirement always passes; otherwise a member without a reported
        /// match time doesn't (an unknown activity is not paid).
        /// </summary>
        public static bool IsActiveEnough(int activeSeconds, int matchSeconds, int minActivePercent)
        {
            var percent = Math.Clamp(minActivePercent, 0, 100);
            if (percent == 0) return true;
            if (matchSeconds <= 0) return false;
            var active = Math.Clamp(activeSeconds, 0, matchSeconds);
            return (long)active * 100 >= (long)percent * matchSeconds;
        }

        private static int NonNegative(int value) => Math.Max(0, value);
    }
}
