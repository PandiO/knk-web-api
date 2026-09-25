using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    /// <summary>Stable codes for SiegeReadinessIssueDto.Code (docs/specs/siege-minigame/DESIGN.md §3.9).</summary>
    public static class SiegeReadinessCodes
    {
        // Errors
        public const string TeamsMinTwo = "TEAMS_MIN_TWO";
        public const string AlliancesMinTwo = "ALLIANCES_MIN_TWO";
        public const string DefenderRequired = "DEFENDER_REQUIRED";
        public const string TeamNoSpawnpoint = "TEAM_NO_SPAWNPOINT";
        public const string TeamIdentityIncomplete = "TEAM_IDENTITY_INCOMPLETE";
        public const string PlayersMinBelowTeamCount = "PLAYERS_MIN_BELOW_TEAM_COUNT";
        public const string PlayersMaxBelowMin = "PLAYERS_MAX_BELOW_MIN";
        public const string DurationRangeInvalid = "DURATION_RANGE_INVALID";
        public const string DistrictOutsideTown = "DISTRICT_OUTSIDE_TOWN";
        public const string HubOutsideTown = "HUB_OUTSIDE_TOWN";
        public const string SpawnpointOutsideTown = "SPAWNPOINT_OUTSIDE_TOWN";
        public const string ObjectiveOutsideTown = "OBJECTIVE_OUTSIDE_TOWN";
        public const string ObjectivesMinOne = "OBJECTIVES_MIN_ONE";
        public const string ObjectiveNoCaptureLocation = "OBJECTIVE_NO_CAPTURE_LOCATION";
        public const string ObjectiveGateNotSelected = "OBJECTIVE_GATE_NOT_SELECTED";
        public const string ObjectiveHolderNotInScenario = "OBJECTIVE_HOLDER_NOT_IN_SCENARIO";
        public const string GateOwnerNotInScenario = "GATE_OWNER_NOT_IN_SCENARIO";
        public const string GateOutsideScenarioArea = "GATE_OUTSIDE_SCENARIO_AREA";

        // Warnings
        public const string NoInstantVictoryObjective = "NO_INSTANT_VICTORY_OBJECTIVE";
        public const string LockdownWithoutDistricts = "LOCKDOWN_WITHOUT_DISTRICTS";
        public const string SpatialChecksSkipped = "SPATIAL_CHECKS_SKIPPED";
        public const string SpatialChecksUnavailable = "SPATIAL_CHECKS_UNAVAILABLE";
        public const string TownHasNoRegion = "TOWN_HAS_NO_REGION";
    }

    /// <summary>
    /// The structural half of scenario validation (docs/specs/siege-minigame/DESIGN.md §3.9): every
    /// rule that can be decided from the loaded scenario graph alone. Pure, so SiegeLobbyService's
    /// runtime-config and SiegeScenarioService's readiness endpoint share one rule set. The spatial
    /// half (hub/spawnpoint/objective locations inside the town's WorldGuard region) needs the plugin
    /// and is added by SiegeScenarioService.
    ///
    /// Expects the graph SiegeScenarioRepository.GetByIdAsync loads (teams with clan + spawnpoints,
    /// objectives with location + gate location, gates with the gate's district, districts).
    /// </summary>
    public static class SiegeScenarioReadiness
    {
        public static SiegeScenarioReadinessDto EvaluateStructure(SiegeScenario scenario)
        {
            var result = new SiegeScenarioReadinessDto { SiegeScenarioId = scenario.Id };
            var teams = scenario.Teams.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToList();
            var teamIds = teams.Select(t => t.Id).ToHashSet();

            // ---- Teams ----
            if (teams.Count < 2)
                Error(result, SiegeReadinessCodes.TeamsMinTwo, $"A scenario needs at least 2 teams (has {teams.Count}).");
            // With fewer than 2 teams TeamsMinTwo already says it all.
            if (teams.Count >= 2 && teams.Select(t => t.AllianceGroup).Distinct().Count() < 2)
                Error(result, SiegeReadinessCodes.AlliancesMinTwo,
                    "Teams need at least 2 different alliance groups - with one group nobody is an enemy.");
            if (!teams.Any(t => t.Role == SiegeTeamRole.Defender))
                Error(result, SiegeReadinessCodes.DefenderRequired,
                    "At least one team must be a Defender (it holds objectives and gates by default).");

            foreach (var team in teams)
            {
                var label = TeamLabel(team);
                if (team.Spawnpoints.Count == 0)
                    Error(result, SiegeReadinessCodes.TeamNoSpawnpoint, $"Team {label} has no spawnpoint.", nameof(SiegeTeam), team.Id);

                var missing = new List<string>();
                if (SiegeTeamIdentity.ResolveName(team) == null) missing.Add("name");
                if (SiegeTeamIdentity.ResolveChatColor(team) == null) missing.Add("chat colour");
                if (SiegeTeamIdentity.ResolveBannerDesignId(team) == null) missing.Add("banner");
                if (missing.Count > 0)
                    Error(result, SiegeReadinessCodes.TeamIdentityIncomplete,
                        $"Team {label} has no clan and no {string.Join(", ", missing)} - a team without a clan needs a name, chat colour and banner.",
                        nameof(SiegeTeam), team.Id);
            }

            // ---- Players / duration ----
            if (scenario.PlayersMin < teams.Count)
                Error(result, SiegeReadinessCodes.PlayersMinBelowTeamCount,
                    $"PlayersMin ({scenario.PlayersMin}) must be at least the number of teams ({teams.Count}).");
            if (scenario.PlayersMax < scenario.PlayersMin)
                Error(result, SiegeReadinessCodes.PlayersMaxBelowMin,
                    $"PlayersMax ({scenario.PlayersMax}) must be at least PlayersMin ({scenario.PlayersMin}).");
            if (scenario.MatchDurationMinSeconds > scenario.MatchDurationMaxSeconds)
                Error(result, SiegeReadinessCodes.DurationRangeInvalid,
                    $"Minimum match duration ({scenario.MatchDurationMinSeconds}s) is above the maximum ({scenario.MatchDurationMaxSeconds}s).");

            // ---- Districts ----
            foreach (var link in scenario.Districts)
            {
                if (link.District != null && link.District.TownId != scenario.TownId)
                    Error(result, SiegeReadinessCodes.DistrictOutsideTown,
                        $"District '{link.District.Name}' doesn't belong to the scenario's town.", nameof(District), link.DistrictId);
            }

            // ---- Objectives ----
            var selectedGateIds = scenario.Gates.Select(g => g.GateStructureId).ToHashSet();
            if (scenario.Objectives.Count == 0)
                Error(result, SiegeReadinessCodes.ObjectivesMinOne, "A scenario needs at least 1 objective.");

            foreach (var objective in scenario.Objectives.OrderBy(o => o.SortOrder).ThenBy(o => o.Id))
            {
                if (CaptureLocationOf(objective) == null)
                    Error(result, SiegeReadinessCodes.ObjectiveNoCaptureLocation,
                        objective.GateStructureId.HasValue
                            ? $"Objective '{objective.Name}' has no location and its gate has none either."
                            : $"Objective '{objective.Name}' needs a capture location or a gate.",
                        nameof(SiegeObjective), objective.Id);

                if (objective.GateStructureId.HasValue && !selectedGateIds.Contains(objective.GateStructureId.Value))
                    Error(result, SiegeReadinessCodes.ObjectiveGateNotSelected,
                        $"Objective '{objective.Name}' uses a gate that isn't one of the scenario's selected gates.",
                        nameof(SiegeObjective), objective.Id);

                if (objective.InitialHolderTeamId.HasValue && !teamIds.Contains(objective.InitialHolderTeamId.Value))
                    Error(result, SiegeReadinessCodes.ObjectiveHolderNotInScenario,
                        $"Objective '{objective.Name}' has an initial holder team from another scenario.",
                        nameof(SiegeObjective), objective.Id);
            }

            if (scenario.Objectives.Count > 0 && !scenario.Objectives.Any(o => o.InstantVictory))
                Warning(result, SiegeReadinessCodes.NoInstantVictoryObjective,
                    "No objective is an instant-victory objective - matches can then only end on time.");

            // ---- Gates ----
            var districtIds = scenario.Districts.Select(d => d.DistrictId).ToHashSet();
            foreach (var gate in scenario.Gates)
            {
                var gateName = gate.GateStructure?.Name ?? $"#{gate.GateStructureId}";
                if (gate.InitialOwnerTeamId.HasValue && !teamIds.Contains(gate.InitialOwnerTeamId.Value))
                    Error(result, SiegeReadinessCodes.GateOwnerNotInScenario,
                        $"Gate '{gateName}' has an initial owner team from another scenario.",
                        nameof(GateStructure), gate.GateStructureId);

                if (gate.GateStructure != null && !IsInScenarioArea(gate.GateStructure, scenario.TownId, districtIds))
                    Error(result, SiegeReadinessCodes.GateOutsideScenarioArea,
                        districtIds.Count > 0
                            ? $"Gate '{gateName}' isn't in one of the scenario's districts."
                            : $"Gate '{gateName}' isn't in the scenario's town.",
                        nameof(GateStructure), gate.GateStructureId);
            }

            if (scenario.LockdownScenarioArea && scenario.Districts.Count == 0)
                Warning(result, SiegeReadinessCodes.LockdownWithoutDistricts,
                    "Area lockdown is on but no districts are selected, so nothing will be locked down.");

            result.IsReady = result.Errors.Count == 0;
            return result;
        }

        /// <summary>The objective's own location, else its gate structure's (DESIGN §3.6).</summary>
        public static Location? CaptureLocationOf(SiegeObjective objective) =>
            objective.Location ?? objective.GateStructure?.Location;

        /// <summary>
        /// "Selected gates belong to the scenario's town/districts" (§3.9): the gate's district must be
        /// one of the scenario's districts when any are selected, else just in the scenario's town.
        /// Needs GateStructure.District loaded.
        /// </summary>
        public static bool IsInScenarioArea(GateStructure gate, int townId, IReadOnlySet<int> scenarioDistrictIds)
        {
            if (scenarioDistrictIds.Count > 0) return scenarioDistrictIds.Contains(gate.DistrictId);
            return gate.District != null && gate.District.TownId == townId;
        }

        public static string TeamLabel(SiegeTeam team) =>
            SiegeTeamIdentity.ResolveName(team) is { } name ? $"'{name}'" : $"#{team.Id}";

        internal static void Error(SiegeScenarioReadinessDto result, string code, string message, string? entityType = null, int? entityId = null) =>
            result.Errors.Add(new SiegeReadinessIssueDto { Code = code, Message = message, EntityType = entityType, EntityId = entityId });

        internal static void Warning(SiegeScenarioReadinessDto result, string code, string message, string? entityType = null, int? entityId = null) =>
            result.Warnings.Add(new SiegeReadinessIssueDto { Code = code, Message = message, EntityType = entityType, EntityId = entityId });
    }
}
