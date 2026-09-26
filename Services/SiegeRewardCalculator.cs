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
    /// </list>
    /// Participants who left before the end get nothing. Negative configured amounts count as 0.
    /// </summary>
    public static class SiegeRewardCalculator
    {
        public sealed record ParticipantInput(int UserId, int? TeamId, bool PresentAtEnd);

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
            int Gems);

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

            return new Reward(participant.UserId, participant.TeamId, true, won, holding, captures, coins, experience, gems);
        }

        private static int NonNegative(int value) => Math.Max(0, value);
    }
}
