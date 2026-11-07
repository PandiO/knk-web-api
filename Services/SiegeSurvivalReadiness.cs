using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    /// <summary>The loaded survival graph of one scenario, as SiegeSurvivalRepository.GetGraphAsync returns it.</summary>
    public sealed class SurvivalScenarioGraph
    {
        public SiegeScenario Scenario { get; init; } = null!;
        public SiegeSurvivalSettings? Settings { get; init; }
        public List<SurvivalDifficultyProfile> DifficultyProfiles { get; init; } = new();
        public List<SurvivalMonsterType> MonsterTypes { get; init; } = new();
        public List<SurvivalSpecialWaveProfile> SpecialWaveProfiles { get; init; } = new();
        public List<SurvivalSpecialWaveEntry> SpecialWaveEntries { get; init; } = new();
        public List<SurvivalSpawnZone> SpawnZones { get; init; } = new();
        public List<SurvivalTargetOverride> TargetOverrides { get; init; } = new();
        public List<SurvivalPurchasePoint> PurchasePoints { get; init; } = new();
        public List<SurvivalMysteryBoxPool> MysteryBoxPools { get; init; } = new();
        public List<SurvivalMysteryBoxPoint> MysteryBoxPoints { get; init; } = new();
        /// <summary>Catalog rows that exist (kits, item blueprints, enchantment definitions), for the reference checks.</summary>
        public IReadOnlySet<int> ExistingKitIds { get; init; } = new HashSet<int>();
        public IReadOnlySet<int> ExistingItemBlueprintIds { get; init; } = new HashSet<int>();
        public IReadOnlySet<int> ExistingEnchantmentDefinitionIds { get; init; } = new HashSet<int>();
    }

    /// <summary>Stable codes of the survival readiness (SiegeReadinessIssueDto.Code), knk-core's strings where one exists.</summary>
    public static class SurvivalReadinessCodes
    {
        public const string SettingsMissing = "settings.missing";
        public const string SettingsDisabled = "settings.disabled";
        public const string SettingsMinPlayersAboveMax = "settings.min-players-above-max";

        public const string TeamHumanMissing = "team.human-missing";
        public const string TeamNpcMissing = "team.npc-missing";
        public const string TeamNotInScenario = "team.not-in-scenario";
        public const string TeamSame = "team.same";
        public const string TeamSameAlliance = "team.same-alliance";

        public const string KitsNoneSelected = "kits.none-selected";
        public const string KitsDefaultMissing = "kits.default-missing";
        public const string KitsDefaultNotSelected = "kits.default-not-selected";
        public const string KitsUnknown = "kits.unknown";
        public const string LoadoutUnknownKit = "loadout.unknown-kit";

        public const string EndWipeOnly = "end.wipe-only";
        public const string EndAllCapturedUnreachable = "end.all-captured-unreachable";

        public const string ProfileNone = "profile.none";
        public const string ProfileUnknown = "profile.unknown";
        public const string ProfileDefaultMissing = "profile.default-missing";
        public const string ProfileMultipleDefault = "profile.multiple-default";

        public const string ScalingCountBase = "scaling.count-base";
        public const string ScalingCountCap = "scaling.count-cap";
        public const string ScalingCountFlat = "scaling.count-flat";
        public const string ScalingStatFlat = "scaling.stat-flat";
        public const string ScalingMultiplier = "scaling.multiplier";
        public const string ScalingSwitchRound = "scaling.switch-round";
        public const string ScalingStatCap = "scaling.stat-cap";
        public const string ScalingRepairRate = "scaling.repair-rate";
        public const string ScalingWalkingSpeedHardCap = "scaling.walking-speed-hard-cap";
        public const string ScalingWalkingSpeedFast = "scaling.walking-speed-fast";

        public const string MixEmpty = "mix.empty";
        public const string MixNoneInRound1 = "mix.none-in-round-1";
        public const string MixDuplicate = "mix.duplicate";

        public const string SpecialUnknownProfile = "special.unknown-profile";
        public const string SpecialExplicitConflict = "special.explicit-conflict";
        public const string SpecialRecurringPriority = "special.recurring-priority";
        public const string SpecialEntryInvalid = "special.entry-invalid";

        public const string SpawnNoInitialZone = "spawn.no-initial-zone";
        public const string SpawnUnknownObjective = "spawn.unknown-objective";
        public const string SpawnZoneUnusable = "spawn.zone-unusable";
        public const string SpawnWaveRange = "spawn.wave-range";
        public const string SpawnLaneSinglePoint = "spawn.lane-single-point";

        public const string TargetInvalid = "target.invalid";
        public const string TargetUnknownObjective = "target.unknown-objective";
        public const string TargetGateNotSelected = "target.gate-not-selected";
        public const string TargetAllObjectivesDenied = "target.all-objectives-denied";

        public const string ShopNoOffers = "shop.no-offers";
        public const string ShopUnknownBlueprint = "shop.unknown-blueprint";

        public const string BoxEmpty = "box.empty";
        public const string BoxChanceRange = "box.chance-range";
        public const string BoxVariant = "box.variant";
        public const string BoxUnknownBlueprint = "box.unknown-blueprint";
        public const string BoxUnknownEnchantment = "box.unknown-enchantment";
        public const string BoxDuplicateEnchantment = "box.duplicate-enchantment";
        public const string BoxTotal = "box.total";
        public const string BoxPoolUnknown = "box.pool-unknown";
        public const string BoxPoolUnused = "box.pool-unused";
    }

    /// <summary>
    /// Survival readiness (knk-workspace docs/specs/siege-survival/DESIGN.md §10 "Readiness validates…", §3a, §6, §7,
    /// §8a; PandiO/knk-workspace#41, link S4): every rule that can be decided from the loaded survival graph alone,
    /// pure, so the readiness endpoint and the frozen configuration share one rule set. The ordinary Siege
    /// structural rules (SiegeScenarioReadiness) are included first - a survival scenario is still a Siege scenario
    /// and the lobby runtime-config only lists scenarios that pass them - except NO_INSTANT_VICTORY_OBJECTIVE, whose
    /// survival meaning is end.wipe-only. Errors block the match start; warnings are shown to the admin.
    /// The codes mirror knk-core's survival ReadinessIssue codes (end.*, scaling.*, mix.*, special.*, spawn.*,
    /// box.*) so the plugin and the web app can map them to one set of messages.
    /// </summary>
    public static class SiegeSurvivalReadiness
    {
        public static SurvivalReadinessDto Evaluate(SurvivalScenarioGraph graph, SurvivalLobbySettings? lobbySettings,
            int? difficultyProfileId)
        {
            var scenario = graph.Scenario;
            var result = new SurvivalReadinessDto
            {
                SiegeScenarioId = scenario.Id,
                SiegeLobbyId = lobbySettings?.SiegeLobbyId,
                DifficultyProfileId = difficultyProfileId
            };

            // ---- Ordinary Siege rules first ----
            var siege = SiegeScenarioReadiness.EvaluateStructure(scenario);
            result.Errors.AddRange(siege.Errors);
            result.Warnings.AddRange(siege.Warnings.Where(w => w.Code != SiegeReadinessCodes.NoInstantVictoryObjective));

            var settings = graph.Settings;
            if (settings == null)
            {
                Error(result, SurvivalReadinessCodes.SettingsMissing, "The scenario has no survival settings - it plays as an ordinary PvP siege.");
                result.IsReady = false;
                return result;
            }
            if (!settings.Enabled)
                Warning(result, SurvivalReadinessCodes.SettingsDisabled, "Survival mode is switched off for this scenario; it plays as an ordinary PvP siege until it is on.");
            if (settings.MinPlayers > scenario.PlayersMax)
                Error(result, SurvivalReadinessCodes.SettingsMinPlayersAboveMax,
                    $"Survival minimum players ({settings.MinPlayers}) is above the scenario's PlayersMax ({scenario.PlayersMax}).");

            // ---- Teams (DESIGN §1, §10 "human/NPC role identity") ----
            var teams = scenario.Teams.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToList();
            var human = ResolveHumanTeam(settings, teams);
            var npc = ResolveNpcTeam(settings, teams);
            if (settings.HumanTeamId.HasValue && teams.All(t => t.Id != settings.HumanTeamId.Value))
                Error(result, SurvivalReadinessCodes.TeamNotInScenario, "The human team is not a team of this scenario.", nameof(SiegeTeam), settings.HumanTeamId);
            if (settings.NpcTeamId.HasValue && teams.All(t => t.Id != settings.NpcTeamId.Value))
                Error(result, SurvivalReadinessCodes.TeamNotInScenario, "The NPC team is not a team of this scenario.", nameof(SiegeTeam), settings.NpcTeamId);
            if (human == null)
                Error(result, SurvivalReadinessCodes.TeamHumanMissing, "No human team: select one or add a Defender team.");
            if (npc == null)
                Error(result, SurvivalReadinessCodes.TeamNpcMissing, "No NPC team: select one or add an Attacker team.");
            if (human != null && npc != null)
            {
                if (human.Id == npc.Id)
                    Error(result, SurvivalReadinessCodes.TeamSame, "The human team and the NPC team are the same team.", nameof(SiegeTeam), human.Id);
                else if (human.AllianceGroup == npc.AllianceGroup)
                    Error(result, SurvivalReadinessCodes.TeamSameAlliance, "The human team and the NPC team are in the same alliance group - nobody is an enemy.");
            }

            // ---- Equipment (DESIGN §8, S23, S38) ----
            var selectedKitIds = settings.SelectedKits.Select(k => k.KitId).ToList();
            foreach (var kitId in selectedKitIds.Where(id => !graph.ExistingKitIds.Contains(id)))
                Error(result, SurvivalReadinessCodes.KitsUnknown, $"Selected kit {kitId} doesn't exist.", nameof(Kit), kitId);
            if (settings.EquipmentMode == SurvivalEquipmentMode.Kits)
            {
                if (selectedKitIds.Count == 0)
                    Error(result, SurvivalReadinessCodes.KitsNoneSelected, "Kits mode needs at least one selected kit.");
                if (!settings.DefaultKitId.HasValue)
                    Error(result, SurvivalReadinessCodes.KitsDefaultMissing, "Kits mode needs a default kit (the kit a player who doesn't pick gets).");
                else if (!selectedKitIds.Contains(settings.DefaultKitId.Value))
                    Error(result, SurvivalReadinessCodes.KitsDefaultNotSelected, "The default kit must be one of the selected kits.", nameof(Kit), settings.DefaultKitId);
            }
            if (settings.EquipmentMode == SurvivalEquipmentMode.MatchProgression
                && settings.BaselineLoadoutKitId.HasValue && !graph.ExistingKitIds.Contains(settings.BaselineLoadoutKitId.Value))
                Error(result, SurvivalReadinessCodes.LoadoutUnknownKit, "The match-progression baseline loadout kit doesn't exist.", nameof(Kit), settings.BaselineLoadoutKitId);

            // ---- End conditions (DESIGN §3a; knk-core EndConditionEvaluator.readiness) ----
            var allCapturedEnabled = ResolveAllCaptured(settings, lobbySettings);
            var anyInstaLose = scenario.Objectives.Any(o => o.InstantVictory);
            if (!anyInstaLose && !allCapturedEnabled)
                Warning(result, SurvivalReadinessCodes.EndWipeOnly,
                    "Only a team wipe can end the game: no objective has the insta-lose flag and \"end when all objectives are captured\" is off.");
            if (allCapturedEnabled && scenario.Objectives.Count == 0)
                Warning(result, SurvivalReadinessCodes.EndAllCapturedUnreachable,
                    "\"End when all objectives are captured\" is on but the scenario has no objective.");

            // ---- Difficulty profiles (DESIGN §7) ----
            var profiles = graph.DifficultyProfiles.OrderBy(p => p.SortOrder).ThenBy(p => p.Id).ToList();
            if (profiles.Count == 0)
                Error(result, SurvivalReadinessCodes.ProfileNone, "The scenario needs at least one difficulty profile.");
            else
            {
                var defaults = profiles.Count(p => p.IsDefault);
                if (defaults == 0)
                    Warning(result, SurvivalReadinessCodes.ProfileDefaultMissing, $"No difficulty profile is the default; '{profiles[0].Name}' (the first) is used.");
                else if (defaults > 1)
                    Warning(result, SurvivalReadinessCodes.ProfileMultipleDefault, "Several difficulty profiles are marked default; the first by order is used.");
            }
            if (difficultyProfileId.HasValue && profiles.All(p => p.Id != difficultyProfileId.Value))
                Error(result, SurvivalReadinessCodes.ProfileUnknown, $"Difficulty profile {difficultyProfileId} is not a profile of this scenario.", nameof(SurvivalDifficultyProfile), difficultyProfileId);

            var fastestBase = graph.MonsterTypes.Count == 0 ? 0 : graph.MonsterTypes.Max(t => SurvivalScaling.VanillaWalkingSpeed(t.EntityType));
            foreach (var p in profiles) CheckProfile(result, p, fastestBase, scenario.PlayersMax);

            // ---- Monster mix (DESIGN §7, S42, S56; knk-core MonsterMix.readiness) ----
            CheckMix(result, graph.MonsterTypes.Select(t => (t.EntityType, t.UnlockRound)).ToList(), nameof(SiegeScenario), scenario.Id, "The scenario");

            // ---- Special waves (DESIGN §7, S14, S53; knk-core SpecialWaveSchedule.readiness) ----
            var profileIds = graph.SpecialWaveProfiles.Select(p => p.Id).ToHashSet();
            var profileKeys = graph.SpecialWaveProfiles.ToDictionary(p => p.Id, p => p.Key);
            var explicitByWave = new Dictionary<int, int>();
            var recurring = new List<SurvivalSpecialWaveEntry>();
            foreach (var entry in graph.SpecialWaveEntries.OrderBy(e => e.SortOrder).ThenBy(e => e.Id))
            {
                if (!profileIds.Contains(entry.SurvivalSpecialWaveProfileId))
                    Error(result, SurvivalReadinessCodes.SpecialUnknownProfile,
                        $"Special-wave entry #{entry.Id} references a profile that isn't one of this scenario's.", nameof(SurvivalSpecialWaveEntry), entry.Id);
                switch (entry.Kind)
                {
                    case SurvivalSpecialWaveKind.Explicit when entry.Wave is >= 1:
                        if (explicitByWave.TryGetValue(entry.Wave.Value, out var previous) && previous != entry.SurvivalSpecialWaveProfileId)
                            Error(result, SurvivalReadinessCodes.SpecialExplicitConflict,
                                $"Wave {entry.Wave} has two explicit special profiles: {profileKeys.GetValueOrDefault(previous, previous.ToString())} and {profileKeys.GetValueOrDefault(entry.SurvivalSpecialWaveProfileId, entry.SurvivalSpecialWaveProfileId.ToString())}.",
                                nameof(SurvivalSpecialWaveEntry), entry.Id);
                        else explicitByWave.TryAdd(entry.Wave.Value, entry.SurvivalSpecialWaveProfileId);
                        break;
                    case SurvivalSpecialWaveKind.Recurring when entry.Interval is >= 1 && (entry.StartWave ?? entry.Interval) >= 1:
                        recurring.Add(entry);
                        break;
                    default:
                        Error(result, SurvivalReadinessCodes.SpecialEntryInvalid,
                            entry.Kind == SurvivalSpecialWaveKind.Explicit
                                ? $"Explicit special-wave entry #{entry.Id} needs a wave number of 1 or more."
                                : $"Recurring special-wave entry #{entry.Id} needs an interval of 1 or more (and a start wave of 1 or more).",
                            nameof(SurvivalSpecialWaveEntry), entry.Id);
                        break;
                }
            }
            for (var i = 0; i < recurring.Count; i++)
            {
                for (var j = i + 1; j < recurring.Count; j++)
                {
                    var a = recurring[i];
                    var b = recurring[j];
                    if (a.Priority == b.Priority && RecurringConflict(a, b))
                        Error(result, SurvivalReadinessCodes.SpecialRecurringPriority,
                            $"Recurring special waves \"every {a.Interval} from {a.StartWave ?? a.Interval}\" ({profileKeys.GetValueOrDefault(a.SurvivalSpecialWaveProfileId, "?")}) and \"every {b.Interval} from {b.StartWave ?? b.Interval}\" ({profileKeys.GetValueOrDefault(b.SurvivalSpecialWaveProfileId, "?")}) can hit the same wave and share priority {a.Priority}.",
                            nameof(SurvivalSpecialWaveEntry), b.Id);
                }
            }
            foreach (var profile in graph.SpecialWaveProfiles)
            {
                // An empty mix means the scenario's normal mix; a non-empty one has the mix rules.
                if (profile.MonsterTypes.Count > 0)
                    CheckMix(result, profile.MonsterTypes.Select(t => (t.EntityType, t.UnlockRound)).ToList(), nameof(SurvivalSpecialWaveProfile), profile.Id, $"Special profile '{profile.Key}'");
            }

            // ---- Spawnzones (DESIGN §6; knk-core SpawnZoneSelector.readiness) ----
            var objectiveIds = scenario.Objectives.Select(o => o.Id).ToHashSet();
            var usableInitial = false;
            foreach (var zone in graph.SpawnZones.OrderBy(z => z.SortOrder).ThenBy(z => z.Id))
            {
                var spawnPoints = zone.Points.Count(p => p.Kind == SurvivalSpawnZonePointKind.Spawn);
                var lanePoints = zone.Points.Count(p => p.Kind == SurvivalSpawnZonePointKind.Lane);
                var usable = spawnPoints > 0 || zone.AreaRadius is > 0;
                if (!usable)
                    Error(result, SurvivalReadinessCodes.SpawnZoneUnusable,
                        $"Spawnzone '{zone.Name}' has no spawnpoint and no spawn area radius.", nameof(SurvivalSpawnZone), zone.Id);
                if (zone.MaxWave.HasValue && zone.MaxWave.Value < zone.MinWave)
                    Error(result, SurvivalReadinessCodes.SpawnWaveRange,
                        $"Spawnzone '{zone.Name}': last wave ({zone.MaxWave}) is before its first wave ({zone.MinWave}).", nameof(SurvivalSpawnZone), zone.Id);
                if (lanePoints == 1)
                    Warning(result, SurvivalReadinessCodes.SpawnLaneSinglePoint,
                        $"Spawnzone '{zone.Name}' has a lane override with a single point; a lane needs at least two to describe a route.", nameof(SurvivalSpawnZone), zone.Id);
                foreach (var prerequisite in zone.PrerequisiteObjectives)
                {
                    if (!objectiveIds.Contains(prerequisite.SiegeObjectiveId))
                        Error(result, SurvivalReadinessCodes.SpawnUnknownObjective,
                            $"Spawnzone '{zone.Name}' references objective {prerequisite.SiegeObjectiveId}, which isn't one of this scenario's.", nameof(SurvivalSpawnZone), zone.Id);
                }
                if (zone.PrerequisiteObjectives.Count == 0 && zone.MinWave <= 1 && zone.ForNormalWaves && usable) usableInitial = true;
            }
            if (!usableInitial)
                Error(result, SurvivalReadinessCodes.SpawnNoInitialZone,
                    "At least one usable spawnzone must be active from the start (no prerequisite objective, first wave 1, normal waves, with a spawnpoint or an area).");

            // ---- Target overrides (DESIGN §4) ----
            var selectedGateIds = scenario.Gates.Select(g => g.GateStructureId).ToHashSet();
            var deniedObjectives = new HashSet<int>();
            foreach (var o in graph.TargetOverrides)
            {
                if (o.SiegeObjectiveId.HasValue == o.GateStructureId.HasValue)
                {
                    Error(result, SurvivalReadinessCodes.TargetInvalid, $"Target override #{o.Id} must name exactly one objective or one gate.", nameof(SurvivalTargetOverride), o.Id);
                    continue;
                }
                if (o.SiegeObjectiveId.HasValue)
                {
                    if (!objectiveIds.Contains(o.SiegeObjectiveId.Value))
                        Error(result, SurvivalReadinessCodes.TargetUnknownObjective, $"Target override #{o.Id} names an objective that isn't one of this scenario's.", nameof(SurvivalTargetOverride), o.Id);
                    else if (o.Mode == SurvivalTargetOverrideMode.Deny) deniedObjectives.Add(o.SiegeObjectiveId.Value);
                }
                else if (!selectedGateIds.Contains(o.GateStructureId!.Value))
                    Error(result, SurvivalReadinessCodes.TargetGateNotSelected, $"Target override #{o.Id} names a gate that isn't one of the scenario's selected gates.", nameof(SurvivalTargetOverride), o.Id);
            }
            if (objectiveIds.Count > 0 && deniedObjectives.Count == objectiveIds.Count)
                Warning(result, SurvivalReadinessCodes.TargetAllObjectivesDenied, "Every objective is denied as an NPC target; NPCs can never capture anything and only a team wipe can end the game.");

            // ---- Purchase points (DESIGN §8) ----
            foreach (var point in graph.PurchasePoints)
            {
                if (point.Offers.Count == 0)
                    Warning(result, SurvivalReadinessCodes.ShopNoOffers, $"Purchase point '{point.Name}' has nothing to sell.", nameof(SurvivalPurchasePoint), point.Id);
                foreach (var offer in point.Offers.Where(o => !graph.ExistingItemBlueprintIds.Contains(o.ItemBlueprintId)))
                    Error(result, SurvivalReadinessCodes.ShopUnknownBlueprint, $"Purchase point '{point.Name}': item blueprint {offer.ItemBlueprintId} doesn't exist.", nameof(SurvivalPurchaseOffer), offer.Id);
            }

            // ---- Mystery Box (DESIGN §8a, S59; knk-core MysteryBoxPool.validate) ----
            var poolIds = graph.MysteryBoxPools.Select(p => p.Id).ToHashSet();
            var usedPoolIds = graph.MysteryBoxPoints.Select(p => p.SurvivalMysteryBoxPoolId).ToHashSet();
            foreach (var pool in graph.MysteryBoxPools) CheckPool(result, pool, graph);
            foreach (var pool in graph.MysteryBoxPools.Where(p => !usedPoolIds.Contains(p.Id)))
                Warning(result, SurvivalReadinessCodes.BoxPoolUnused, $"Mystery Box pool '{pool.Name}' isn't used by any Mystery Box point.", nameof(SurvivalMysteryBoxPool), pool.Id);
            foreach (var point in graph.MysteryBoxPoints.Where(p => !poolIds.Contains(p.SurvivalMysteryBoxPoolId)))
                Error(result, SurvivalReadinessCodes.BoxPoolUnknown, $"Mystery Box '{point.Name}' uses a pool that isn't one of this scenario's.", nameof(SurvivalMysteryBoxPoint), point.Id);

            result.IsReady = result.Errors.Count == 0;
            return result;
        }

        private static void CheckProfile(SurvivalReadinessDto result, SurvivalDifficultyProfile p, double fastestBaseSpeed, int maxPlayers)
        {
            var type = nameof(SurvivalDifficultyProfile);
            var label = $"Difficulty '{p.Name}'";
            if (p.CountBase < 1)
                Error(result, SurvivalReadinessCodes.ScalingCountBase, $"{label}: the round-1 enemy count must be at least 1.", type, p.Id);
            if (UsesMultiplier(p.CountPreset) && p.CountMultiplier <= 0)
                Error(result, SurvivalReadinessCodes.ScalingMultiplier, $"{label}: the count multiplier must be above 0.", type, p.Id);
            if (UsesMultiplier(p.StatPreset) && p.StatMultiplier <= 0)
                Error(result, SurvivalReadinessCodes.ScalingMultiplier, $"{label}: the stat multiplier must be above 0.", type, p.Id);
            if (p.CountPreset == SurvivalScalingPreset.Hybrid && p.CountSwitchRound < 1)
                Error(result, SurvivalReadinessCodes.ScalingSwitchRound, $"{label}: the count switch round must be at least 1.", type, p.Id);
            if (p.StatPreset == SurvivalScalingPreset.Hybrid && p.StatSwitchRound < 1)
                Error(result, SurvivalReadinessCodes.ScalingSwitchRound, $"{label}: the stat switch round must be at least 1.", type, p.Id);
            if (p.StatCap < 1)
                Error(result, SurvivalReadinessCodes.ScalingStatCap, $"{label}: the stat cap must be at least 1 (round 1 is vanilla).", type, p.Id);
            if (p.CountCap > 0 && p.CountCap < p.CountBase)
                Error(result, SurvivalReadinessCodes.ScalingCountCap, $"{label}: count cap {p.CountCap} is below the round-1 count.", type, p.Id);
            if (p.RepairRatePerSecond <= 0)
                Error(result, SurvivalReadinessCodes.ScalingRepairRate, $"{label}: the repair rate must be above 0.", type, p.Id);
            if (!SurvivalScaling.Grows(p.CountPreset, p.CountStep, p.CountMultiplier))
                Warning(result, SurvivalReadinessCodes.ScalingCountFlat, $"{label}: the enemy count does not grow with the rounds.", type, p.Id);
            if (!SurvivalScaling.Grows(p.StatPreset, p.StatStep, p.StatMultiplier))
                Warning(result, SurvivalReadinessCodes.ScalingStatFlat, $"{label}: the enemy stats do not grow with the rounds.", type, p.Id);

            if (fastestBaseSpeed > 0 && p.StatCap >= 1)
            {
                var multiplier = SurvivalScaling.MaxWalkingSpeedMultiplier(p, maxPlayers);
                var effective = fastestBaseSpeed * multiplier;
                // The NPC platform clamps the speed at the hard cap at runtime, so both are warnings: the admin sees
                // that the configured growth will be cut off (or breaks pathfinding on mobs that aren't clamped).
                if (effective > SurvivalScaling.WalkingSpeedHardCap)
                    Warning(result, SurvivalReadinessCodes.ScalingWalkingSpeedHardCap,
                        $"{label}: walking speed {fastestBaseSpeed:0.###} × {multiplier:0.##} = {effective:0.###} exceeds the hard cap {SurvivalScaling.WalkingSpeedHardCap:0.##} (clamped at runtime; pathfinding breaks above it). Lower the stat cap or the walking-speed weight.",
                        type, p.Id);
                else if (effective > SurvivalScaling.WalkingSpeedRecommended)
                    Warning(result, SurvivalReadinessCodes.ScalingWalkingSpeedFast,
                        $"{label}: walking speed {fastestBaseSpeed:0.###} × {multiplier:0.##} = {effective:0.###} is above the recommended {SurvivalScaling.WalkingSpeedRecommended:0.##}.",
                        type, p.Id);
            }
        }

        private static bool UsesMultiplier(SurvivalScalingPreset preset) =>
            preset is SurvivalScalingPreset.Exponential or SurvivalScalingPreset.Hybrid;

        private static void CheckMix(SurvivalReadinessDto result, List<(string EntityType, int UnlockRound)> types, string entityType, int entityId, string label)
        {
            if (types.Count == 0)
            {
                Error(result, SurvivalReadinessCodes.MixEmpty, $"{label} needs at least one hostile monster type.", entityType, entityId);
                return;
            }
            if (types.All(t => t.UnlockRound > 1))
                Error(result, SurvivalReadinessCodes.MixNoneInRound1, $"{label}: no selected monster type is unlocked in round 1.", entityType, entityId);
            foreach (var duplicate in types.GroupBy(t => t.EntityType.Trim().ToUpperInvariant()).Where(g => g.Count() > 1))
                Error(result, SurvivalReadinessCodes.MixDuplicate, $"{label}: monster type {duplicate.Key} is selected twice.", entityType, entityId);
        }

        /// <summary>
        /// The pool rules alone (DESIGN §8a, S59: entries valid for the variant, references known, every chance in
        /// range, total exactly 100 %), for the match-scoped roll (link S7a): the box.* errors, empty when the pool
        /// is valid. The same rules as the scenario readiness.
        /// </summary>
        public static List<SiegeReadinessIssueDto> EvaluatePool(SurvivalMysteryBoxPool pool, HashSet<int> existingItemBlueprintIds, HashSet<int> existingEnchantmentDefinitionIds)
        {
            var result = new SurvivalReadinessDto();
            CheckPool(result, pool, new SurvivalScenarioGraph
            {
                ExistingItemBlueprintIds = existingItemBlueprintIds,
                ExistingEnchantmentDefinitionIds = existingEnchantmentDefinitionIds
            });
            return result.Errors;
        }

        private static void CheckPool(SurvivalReadinessDto result, SurvivalMysteryBoxPool pool, SurvivalScenarioGraph graph)
        {
            var type = nameof(SurvivalMysteryBoxPool);
            var label = $"Mystery Box pool '{pool.Name}'";
            if (pool.Entries.Count == 0)
            {
                Error(result, SurvivalReadinessCodes.BoxEmpty, $"{label} has no entries.", type, pool.Id);
                return;
            }
            var total = 0;
            foreach (var entry in pool.Entries.OrderBy(e => e.SortOrder).ThenBy(e => e.Id))
            {
                var bp = SafeBasisPoints(entry.ChancePercent);
                total += bp;
                var describe = entry.ItemBlueprintId.HasValue ? $"item {entry.ItemBlueprintId}" : $"book {entry.EnchantmentDefinitionId} {entry.EnchantmentLevel}";
                if (bp < 1 || bp > 10_000)
                    Error(result, SurvivalReadinessCodes.BoxChanceRange, $"{label}, {describe}: chance must be between 0.01 % and 100 %.", nameof(SurvivalMysteryBoxEntry), entry.Id);

                var isItem = entry.ItemBlueprintId.HasValue;
                var isBook = entry.EnchantmentDefinitionId.HasValue && !isItem;
                if (isItem)
                {
                    if (pool.Variant != SurvivalMysteryBoxVariant.Items)
                        Error(result, SurvivalReadinessCodes.BoxVariant, $"{label}, {describe} does not belong in an enchanted-book pool.", nameof(SurvivalMysteryBoxEntry), entry.Id);
                    if (!graph.ExistingItemBlueprintIds.Contains(entry.ItemBlueprintId!.Value))
                        Error(result, SurvivalReadinessCodes.BoxUnknownBlueprint, $"{label}: unknown item blueprint {entry.ItemBlueprintId}.", nameof(SurvivalMysteryBoxEntry), entry.Id);
                    var seen = new HashSet<int>();
                    foreach (var chance in entry.Enchantments)
                    {
                        if (!seen.Add(chance.EnchantmentDefinitionId))
                            Error(result, SurvivalReadinessCodes.BoxDuplicateEnchantment, $"{label}, {describe}: enchantment {chance.EnchantmentDefinitionId} listed twice.", nameof(SurvivalMysteryBoxEntry), entry.Id);
                        if (!graph.ExistingEnchantmentDefinitionIds.Contains(chance.EnchantmentDefinitionId))
                            Error(result, SurvivalReadinessCodes.BoxUnknownEnchantment, $"{label}: unknown enchantment {chance.EnchantmentDefinitionId}.", nameof(SurvivalMysteryBoxEntry), entry.Id);
                        var chanceBp = SafeBasisPoints(chance.ChancePercent);
                        if (chanceBp < 1 || chanceBp > 10_000)
                            Error(result, SurvivalReadinessCodes.BoxChanceRange, $"{label}, {describe}: enchantment chance must be between 0.01 % and 100 %.", nameof(SurvivalMysteryBoxEntry), entry.Id);
                    }
                }
                else if (isBook)
                {
                    if (pool.Variant != SurvivalMysteryBoxVariant.EnchantedBooks)
                        Error(result, SurvivalReadinessCodes.BoxVariant, $"{label}, {describe} does not belong in an item pool.", nameof(SurvivalMysteryBoxEntry), entry.Id);
                    if (!graph.ExistingEnchantmentDefinitionIds.Contains(entry.EnchantmentDefinitionId!.Value))
                        Error(result, SurvivalReadinessCodes.BoxUnknownEnchantment, $"{label}: unknown enchantment {entry.EnchantmentDefinitionId}.", nameof(SurvivalMysteryBoxEntry), entry.Id);
                    if (entry.EnchantmentLevel is null or < 1)
                        Error(result, SurvivalReadinessCodes.BoxVariant, $"{label}, {describe}: an enchanted-book entry needs a level of 1 or more.", nameof(SurvivalMysteryBoxEntry), entry.Id);
                }
                else
                {
                    Error(result, SurvivalReadinessCodes.BoxVariant, $"{label}: entry #{entry.Id} names neither an item blueprint nor an enchantment.", nameof(SurvivalMysteryBoxEntry), entry.Id);
                }
            }
            if (total != 10_000)
                Error(result, SurvivalReadinessCodes.BoxTotal, $"{label}: chances add up to {total / 100.0:0.##} %, not exactly 100 %.", type, pool.Id);
        }

        private static int SafeBasisPoints(decimal percent)
        {
            try { return SurvivalScaling.ToBasisPoints(percent); }
            catch (ArgumentException) { return -1; }
        }

        /// <summary>Whether two recurring entries can select a common wave (offsets agree modulo the gcd of the intervals).</summary>
        public static bool RecurringConflict(SurvivalSpecialWaveEntry a, SurvivalSpecialWaveEntry b)
        {
            var ia = a.Interval ?? 1;
            var ib = b.Interval ?? 1;
            var sa = a.StartWave ?? ia;
            var sb = b.StartWave ?? ib;
            var g = Gcd(ia, ib);
            return ((sa - sb) % g + g) % g == 0;
        }

        private static int Gcd(int a, int b)
        {
            while (b != 0) (a, b) = (b, a % b);
            return a;
        }

        public static SiegeTeam? ResolveHumanTeam(SiegeSurvivalSettings settings, IReadOnlyList<SiegeTeam> teams) =>
            settings.HumanTeamId.HasValue
                ? teams.FirstOrDefault(t => t.Id == settings.HumanTeamId.Value)
                : teams.FirstOrDefault(t => t.Role == SiegeTeamRole.Defender);

        public static SiegeTeam? ResolveNpcTeam(SiegeSurvivalSettings settings, IReadOnlyList<SiegeTeam> teams) =>
            settings.NpcTeamId.HasValue
                ? teams.FirstOrDefault(t => t.Id == settings.NpcTeamId.Value)
                : teams.FirstOrDefault(t => t.Role == SiegeTeamRole.Attacker);

        /// <summary>The resolved "end when all objectives are captured" (lobby → scenario, S46).</summary>
        public static bool ResolveAllCaptured(SiegeSurvivalSettings settings, SurvivalLobbySettings? lobby) =>
            (lobby?.AllCapturedOverride ?? SurvivalAllCapturedOverride.Inherit) switch
            {
                SurvivalAllCapturedOverride.On => true,
                SurvivalAllCapturedOverride.Off => false,
                _ => settings.EndWhenAllObjectivesCaptured
            };

        internal static void Error(SurvivalReadinessDto result, string code, string message, string? entityType = null, int? entityId = null) =>
            result.Errors.Add(new SiegeReadinessIssueDto { Code = code, Message = message, EntityType = entityType, EntityId = entityId });

        internal static void Warning(SurvivalReadinessDto result, string code, string message, string? entityType = null, int? entityId = null) =>
            result.Warnings.Add(new SiegeReadinessIssueDto { Code = code, Message = message, EntityType = entityType, EntityId = entityId });
    }
}
