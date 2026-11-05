using System.Linq.Expressions;
using System.Text.RegularExpressions;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    // Siege Survival phase 1 (knk-workspace docs/specs/siege-survival/DESIGN.md §3a, §6–§8a, §10;
    // PandiO/knk-workspace#41, link S4). The survival aggregate of a scenario: its settings row, the survival child
    // collections (each with its own endpoints) and the survival lobby settings. Like the Siege scenario, a survival
    // scenario is authored in several saves, so each row's own fields are checked on save and the cross-row rules
    // ("at least one monster type", "pool adds up to 100 %") are readiness (SiegeSurvivalReadiness). The frozen
    // configuration resolves every default so the plugin never re-implements them.
    public class SiegeSurvivalService : ISiegeSurvivalService
    {
        private static readonly Regex EntityTypePattern = new("^[A-Z0-9_]{1,64}$", RegexOptions.Compiled);
        private static readonly Regex ProfileKeyPattern = new("^[a-z0-9][a-z0-9_-]{0,63}$", RegexOptions.Compiled);
        private static readonly Regex BlockKeyPattern = new("^[a-z0-9_.-]+:[a-z0-9_/.-]+$", RegexOptions.Compiled);

        private readonly ISiegeSurvivalRepository _repo;
        private readonly ISiegeScenarioRepository _scenarioRepo;
        private readonly ILocationService _locationService;
        private readonly IMapper _mapper;

        public SiegeSurvivalService(
            ISiegeSurvivalRepository repo,
            ISiegeScenarioRepository scenarioRepo,
            ILocationService locationService,
            IMapper mapper)
        {
            _repo = repo;
            _scenarioRepo = scenarioRepo;
            _locationService = locationService;
            _mapper = mapper;

            DifficultyProfiles = new ChildOps<SurvivalDifficultyProfile, SurvivalDifficultyProfileDto, SurvivalDifficultyProfileUpsertDto>(
                this, pid => p => p.SiegeScenarioId == pid, id => _repo.FindAsync<SurvivalDifficultyProfile>(id),
                ApplyDifficultyProfileAsync, p => p.SortOrder);
            MonsterTypes = new ChildOps<SurvivalMonsterType, SurvivalMonsterTypeDto, SurvivalMonsterTypeUpsertDto>(
                this, pid => m => m.SiegeScenarioId == pid, id => _repo.FindAsync<SurvivalMonsterType>(id),
                ApplyMonsterTypeAsync, m => m.SortOrder);
            SpecialWaveProfiles = new ChildOps<SurvivalSpecialWaveProfile, SurvivalSpecialWaveProfileDto, SurvivalSpecialWaveProfileUpsertDto>(
                this, pid => p => p.SiegeScenarioId == pid, _repo.GetSpecialWaveProfileAsync,
                ApplySpecialWaveProfileAsync, p => p.SortOrder);
            SpecialWaveMonsterTypes = new ChildOps<SurvivalSpecialWaveMonsterType, SurvivalSpecialWaveMonsterTypeDto, SurvivalSpecialWaveMonsterTypeUpsertDto>(
                this, pid => m => m.SurvivalSpecialWaveProfileId == pid, id => _repo.FindAsync<SurvivalSpecialWaveMonsterType>(id),
                ApplySpecialWaveMonsterTypeAsync, m => m.SortOrder);
            SpecialWaveEntries = new ChildOps<SurvivalSpecialWaveEntry, SurvivalSpecialWaveEntryDto, SurvivalSpecialWaveEntryUpsertDto>(
                this, pid => e => e.SiegeScenarioId == pid, _repo.GetSpecialWaveEntryAsync,
                ApplySpecialWaveEntryAsync, e => e.SortOrder);
            SpawnZones = new ChildOps<SurvivalSpawnZone, SurvivalSpawnZoneDto, SurvivalSpawnZoneUpsertDto>(
                this, pid => z => z.SiegeScenarioId == pid, _repo.GetSpawnZoneAsync,
                ApplySpawnZoneAsync, z => z.SortOrder);
            SpawnZonePoints = new ChildOps<SurvivalSpawnZonePoint, SurvivalSpawnZonePointDto, SurvivalSpawnZonePointUpsertDto>(
                this, pid => p => p.SurvivalSpawnZoneId == pid, _repo.GetSpawnZonePointAsync,
                ApplySpawnZonePointAsync, p => p.SortOrder);
            TargetOverrides = new ChildOps<SurvivalTargetOverride, SurvivalTargetOverrideDto, SurvivalTargetOverrideUpsertDto>(
                this, pid => o => o.SiegeScenarioId == pid, _repo.GetTargetOverrideAsync,
                ApplyTargetOverrideAsync, o => o.Id);
            PurchasePoints = new ChildOps<SurvivalPurchasePoint, SurvivalPurchasePointDto, SurvivalPurchasePointUpsertDto>(
                this, pid => p => p.SiegeScenarioId == pid, _repo.GetPurchasePointAsync,
                ApplyPurchasePointAsync, p => p.SortOrder);
            PurchaseOffers = new ChildOps<SurvivalPurchaseOffer, SurvivalPurchaseOfferDto, SurvivalPurchaseOfferUpsertDto>(
                this, pid => o => o.SurvivalPurchasePointId == pid, _repo.GetPurchaseOfferAsync,
                ApplyPurchaseOfferAsync, o => o.SortOrder);
            MysteryBoxPools = new ChildOps<SurvivalMysteryBoxPool, SurvivalMysteryBoxPoolDto, SurvivalMysteryBoxPoolUpsertDto>(
                this, pid => p => p.SiegeScenarioId == pid, _repo.GetMysteryBoxPoolAsync,
                ApplyMysteryBoxPoolAsync, p => p.Id, beforeDelete: GuardPoolDeleteAsync);
            MysteryBoxEntries = new ChildOps<SurvivalMysteryBoxEntry, SurvivalMysteryBoxEntryDto, SurvivalMysteryBoxEntryUpsertDto>(
                this, pid => e => e.SurvivalMysteryBoxPoolId == pid, _repo.GetMysteryBoxEntryAsync,
                ApplyMysteryBoxEntryAsync, e => e.SortOrder);
            MysteryBoxPoints = new ChildOps<SurvivalMysteryBoxPoint, SurvivalMysteryBoxPointDto, SurvivalMysteryBoxPointUpsertDto>(
                this, pid => p => p.SiegeScenarioId == pid, _repo.GetMysteryBoxPointAsync,
                ApplyMysteryBoxPointAsync, p => p.SortOrder);
        }

        public ISurvivalChildOperations<SurvivalDifficultyProfileDto, SurvivalDifficultyProfileUpsertDto> DifficultyProfiles { get; }
        public ISurvivalChildOperations<SurvivalMonsterTypeDto, SurvivalMonsterTypeUpsertDto> MonsterTypes { get; }
        public ISurvivalChildOperations<SurvivalSpecialWaveProfileDto, SurvivalSpecialWaveProfileUpsertDto> SpecialWaveProfiles { get; }
        public ISurvivalChildOperations<SurvivalSpecialWaveMonsterTypeDto, SurvivalSpecialWaveMonsterTypeUpsertDto> SpecialWaveMonsterTypes { get; }
        public ISurvivalChildOperations<SurvivalSpecialWaveEntryDto, SurvivalSpecialWaveEntryUpsertDto> SpecialWaveEntries { get; }
        public ISurvivalChildOperations<SurvivalSpawnZoneDto, SurvivalSpawnZoneUpsertDto> SpawnZones { get; }
        public ISurvivalChildOperations<SurvivalSpawnZonePointDto, SurvivalSpawnZonePointUpsertDto> SpawnZonePoints { get; }
        public ISurvivalChildOperations<SurvivalTargetOverrideDto, SurvivalTargetOverrideUpsertDto> TargetOverrides { get; }
        public ISurvivalChildOperations<SurvivalPurchasePointDto, SurvivalPurchasePointUpsertDto> PurchasePoints { get; }
        public ISurvivalChildOperations<SurvivalPurchaseOfferDto, SurvivalPurchaseOfferUpsertDto> PurchaseOffers { get; }
        public ISurvivalChildOperations<SurvivalMysteryBoxPoolDto, SurvivalMysteryBoxPoolUpsertDto> MysteryBoxPools { get; }
        public ISurvivalChildOperations<SurvivalMysteryBoxEntryDto, SurvivalMysteryBoxEntryUpsertDto> MysteryBoxEntries { get; }
        public ISurvivalChildOperations<SurvivalMysteryBoxPointDto, SurvivalMysteryBoxPointUpsertDto> MysteryBoxPoints { get; }

        // ==== Scenario settings ====

        public async Task<List<SurvivalScenarioSummaryDto>> GetScenarioSummariesAsync()
        {
            var counts = await _repo.CountDifficultyProfilesAsync();
            return (await _repo.GetAllSettingsAsync()).Select(s => new SurvivalScenarioSummaryDto
            {
                SiegeScenarioId = s.SiegeScenarioId,
                SiegeScenarioName = s.SiegeScenario?.Name,
                Enabled = s.Enabled,
                EquipmentMode = s.EquipmentMode,
                DifficultyProfileCount = counts.GetValueOrDefault(s.SiegeScenarioId)
            }).ToList();
        }

        public async Task<SiegeSurvivalSettingsReadDto?> GetSettingsAsync(int siegeScenarioId)
        {
            if (siegeScenarioId <= 0) return null;
            var settings = await _repo.GetSettingsAsync(siegeScenarioId);
            return settings == null ? null : _mapper.Map<SiegeSurvivalSettingsReadDto>(settings);
        }

        public async Task<SiegeSurvivalSettingsReadDto> UpsertSettingsAsync(int siegeScenarioId, SiegeSurvivalSettingsUpsertDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var scenario = await RequireScenarioAsync(siegeScenarioId);
            var existing = await _repo.GetSettingsAsync(siegeScenarioId);
            var target = existing ?? new SiegeSurvivalSettings { SiegeScenarioId = siegeScenarioId };

            if (dto.MinPlayers < 1) throw new ArgumentException("minPlayers must be at least 1.");
            if (!Enum.IsDefined(dto.EquipmentMode)) throw new ArgumentException("equipmentMode must be OwnInventory, Kits or MatchProgression.");
            int? humanTeamId = dto.HumanTeamId is > 0 ? dto.HumanTeamId : null;
            int? npcTeamId = dto.NpcTeamId is > 0 ? dto.NpcTeamId : null;
            if (humanTeamId.HasValue && scenario.Teams.All(t => t.Id != humanTeamId.Value))
                throw new ArgumentException("humanTeamId must be a team of this scenario.");
            if (npcTeamId.HasValue && scenario.Teams.All(t => t.Id != npcTeamId.Value))
                throw new ArgumentException("npcTeamId must be a team of this scenario.");
            if (humanTeamId.HasValue && humanTeamId == npcTeamId)
                throw new ArgumentException("The human team and the NPC team must be different teams.");
            if (dto.IntermissionMillis < 0) throw new ArgumentException("intermissionMillis can't be negative.");
            if (dto.FavorNearPlayersPercent is < 0 or > 100) throw new ArgumentException("favorNearPlayersPercent must be between 0 and 100.");
            if (dto.NearZoneCount < 1) throw new ArgumentException("nearZoneCount must be at least 1.");
            if (dto.StuckAfterMillis <= 0) throw new ArgumentException("stuckAfterMillis must be above 0.");
            if (dto.LastEnemiesCount < 0) throw new ArgumentException("lastEnemiesCount can't be negative.");
            if (dto.RespawnLimit < 0) throw new ArgumentException("respawnLimit can't be negative.");
            if (dto.RoundBackstopMillis is <= 0) throw new ArgumentException("roundBackstopMillis must be above 0, or empty for no backstop.");
            if (!(dto.MeaningfulDistance > 0) || double.IsInfinity(dto.MeaningfulDistance)) throw new ArgumentException("meaningfulDistance must be above 0.");
            if (dto.CoinsPerKill is < 0) throw new ArgumentException("coinsPerKill can't be negative.");
            if (dto.XpPerKill is < 0) throw new ArgumentException("xpPerKill can't be negative.");
            if (dto.MatchCurrencyPerKill < 0) throw new ArgumentException("matchCurrencyPerKill can't be negative.");
            if (dto.KillBonusMultiplier <= 0) throw new ArgumentException("killBonusMultiplier must be above 0.");

            int? defaultKitId = dto.DefaultKitId is > 0 ? dto.DefaultKitId : null;
            int? baselineKitId = dto.BaselineLoadoutKitId is > 0 ? dto.BaselineLoadoutKitId : null;
            List<SurvivalSelectedKitUpsertDto>? kits = null;
            if (dto.SelectedKits != null)
            {
                kits = dto.SelectedKits.Where(k => k != null && k.KitId > 0).ToList();
                var duplicate = kits.GroupBy(k => k.KitId).FirstOrDefault(g => g.Count() > 1);
                if (duplicate != null) throw new ArgumentException($"Kit {duplicate.Key} is selected twice.");
            }
            var wantedKitIds = (kits?.Select(k => k.KitId) ?? target.SelectedKits.Select(k => k.KitId)).ToList();
            if (defaultKitId.HasValue) wantedKitIds.Add(defaultKitId.Value);
            if (baselineKitId.HasValue) wantedKitIds.Add(baselineKitId.Value);
            var existingKits = await _repo.ExistingKitIdsAsync(wantedKitIds);
            var missingKit = wantedKitIds.FirstOrDefault(id => !existingKits.Contains(id));
            if (missingKit != 0) throw new ArgumentException($"Kit with id {missingKit} not found.");

            target.Enabled = dto.Enabled;
            target.MinPlayers = dto.MinPlayers;
            target.HumanTeamId = humanTeamId;
            target.NpcTeamId = npcTeamId;
            target.EquipmentMode = dto.EquipmentMode;
            target.DefaultKitId = defaultKitId;
            target.BaselineLoadoutKitId = baselineKitId;
            target.IntermissionMillis = dto.IntermissionMillis;
            target.EndWhenAllObjectivesCaptured = dto.EndWhenAllObjectivesCaptured;
            target.FavorNearPlayersPercent = dto.FavorNearPlayersPercent;
            target.NearZoneCount = dto.NearZoneCount;
            target.StuckAfterMillis = dto.StuckAfterMillis;
            target.LastEnemiesCount = dto.LastEnemiesCount;
            target.RespawnLimit = dto.RespawnLimit;
            target.RoundBackstopMillis = dto.RoundBackstopMillis;
            target.MeaningfulDistance = dto.MeaningfulDistance;
            target.CoinsPerKill = dto.CoinsPerKill;
            target.XpPerKill = dto.XpPerKill;
            target.MatchCurrencyPerKill = dto.MatchCurrencyPerKill;
            target.KillBonusMultiplier = dto.KillBonusMultiplier;

            if (kits != null)
            {
                // Diff instead of Clear()+re-add (composite key tracking, as the Siege joins).
                var wanted = kits.Select(k => k.KitId).ToHashSet();
                foreach (var stale in target.SelectedKits.Where(k => !wanted.Contains(k.KitId)).ToList())
                    target.SelectedKits.Remove(stale);
                var order = 0;
                foreach (var kit in kits)
                {
                    var row = target.SelectedKits.FirstOrDefault(k => k.KitId == kit.KitId);
                    if (row == null)
                    {
                        row = new SurvivalSettingsKit { SiegeSurvivalSettingsId = target.Id, KitId = kit.KitId };
                        target.SelectedKits.Add(row);
                    }
                    row.SortOrder = kit.SortOrder ?? order;
                    order++;
                }
            }

            if (existing == null) await _repo.AddAsync(target);
            else await _repo.SaveAsync(target);

            var saved = await _repo.GetSettingsAsync(siegeScenarioId);
            return _mapper.Map<SiegeSurvivalSettingsReadDto>(saved ?? target);
        }

        public async Task DeleteSettingsAsync(int siegeScenarioId)
        {
            var existing = await _repo.GetSettingsAsync(siegeScenarioId)
                ?? throw new KeyNotFoundException($"SiegeScenario {siegeScenarioId} has no survival settings.");
            // The survival children stay with the scenario; only the settings row (and its kit joins) go.
            await _repo.RemoveAsync(existing);
        }

        // ==== Lobby settings ====

        public async Task<SurvivalLobbySettingsReadDto?> GetLobbySettingsAsync(int siegeLobbyId)
        {
            if (siegeLobbyId <= 0) return null;
            var settings = await _repo.GetLobbySettingsAsync(siegeLobbyId);
            return settings == null ? null : _mapper.Map<SurvivalLobbySettingsReadDto>(settings);
        }

        public async Task<SurvivalLobbySettingsReadDto> UpsertLobbySettingsAsync(int siegeLobbyId, SurvivalLobbySettingsUpsertDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            _ = await _repo.GetLobbyAsync(siegeLobbyId) ?? throw new KeyNotFoundException($"SiegeLobby with id {siegeLobbyId} not found.");
            if (!Enum.IsDefined(dto.AllCapturedOverride)) throw new ArgumentException("allCapturedOverride must be Inherit, On or Off.");

            var existing = await _repo.GetLobbySettingsAsync(siegeLobbyId);
            var target = existing ?? new SurvivalLobbySettings { SiegeLobbyId = siegeLobbyId };
            target.AllCapturedOverride = dto.AllCapturedOverride;
            if (existing == null) await _repo.AddAsync(target);
            else await _repo.SaveAsync(target);

            var saved = await _repo.GetLobbySettingsAsync(siegeLobbyId);
            return _mapper.Map<SurvivalLobbySettingsReadDto>(saved ?? target);
        }

        public async Task DeleteLobbySettingsAsync(int siegeLobbyId)
        {
            var existing = await _repo.GetLobbySettingsAsync(siegeLobbyId)
                ?? throw new KeyNotFoundException($"SiegeLobby {siegeLobbyId} has no survival settings.");
            await _repo.RemoveAsync(existing);
        }

        // ==== Frozen configuration, readiness, preview ====

        public async Task<SurvivalReadinessDto> GetReadinessAsync(int siegeScenarioId, int? siegeLobbyId, int? difficultyProfileId)
        {
            var (graph, lobbySettings, profile) = await LoadForMatchAsync(siegeScenarioId, siegeLobbyId, difficultyProfileId, requireSettings: false);
            return SiegeSurvivalReadiness.Evaluate(graph, lobbySettings, profile?.Id ?? difficultyProfileId);
        }

        public async Task<SurvivalConfigurationDto> GetConfigurationAsync(int siegeScenarioId, int? siegeLobbyId, int? difficultyProfileId)
        {
            var (graph, lobbySettings, profile) = await LoadForMatchAsync(siegeScenarioId, siegeLobbyId, difficultyProfileId, requireSettings: true);
            var scenario = graph.Scenario;
            var settings = graph.Settings!;
            var teams = scenario.Teams.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToList();
            var objectiveOverrides = graph.TargetOverrides.Where(o => o.SiegeObjectiveId.HasValue)
                .GroupBy(o => o.SiegeObjectiveId!.Value).ToDictionary(g => g.Key, g => g.First().Mode);
            var gateOverrides = graph.TargetOverrides.Where(o => o.GateStructureId.HasValue)
                .GroupBy(o => o.GateStructureId!.Value).ToDictionary(g => g.Key, g => g.First().Mode);
            var objectiveGateIds = scenario.Objectives.Where(o => o.GateStructureId.HasValue).Select(o => o.GateStructureId!.Value).ToHashSet();
            var lobbyOverride = lobbySettings?.AllCapturedOverride ?? SurvivalAllCapturedOverride.Inherit;

            return new SurvivalConfigurationDto
            {
                GeneratedAt = DateTime.UtcNow,
                SiegeScenarioId = scenario.Id,
                SiegeScenarioName = scenario.Name,
                SiegeLobbyId = siegeLobbyId,
                Enabled = settings.Enabled,
                MinPlayers = settings.MinPlayers,
                HumanTeamId = SiegeSurvivalReadiness.ResolveHumanTeam(settings, teams)?.Id,
                NpcTeamId = SiegeSurvivalReadiness.ResolveNpcTeam(settings, teams)?.Id,
                AllowRecapture = scenario.AllowRecapture,
                IntermissionMillis = settings.IntermissionMillis,
                Equipment = new SurvivalEquipmentConfigDto
                {
                    Mode = settings.EquipmentMode,
                    SelectedKits = _mapper.Map<List<SurvivalSelectedKitDto>>(settings.SelectedKits.OrderBy(k => k.SortOrder).ThenBy(k => k.KitId)),
                    DefaultKitId = settings.DefaultKitId,
                    BaselineLoadoutKitId = settings.BaselineLoadoutKitId
                },
                EndConditions = new SurvivalEndConditionsConfigDto
                {
                    ScenarioAllCaptured = settings.EndWhenAllObjectivesCaptured,
                    LobbyOverride = lobbyOverride,
                    AllCapturedEnabled = SiegeSurvivalReadiness.ResolveAllCaptured(settings, lobbySettings),
                    InstaLoseObjectiveIds = scenario.Objectives.Where(o => o.InstantVictory).OrderBy(o => o.SortOrder).ThenBy(o => o.Id).Select(o => o.Id).ToList()
                },
                Objectives = scenario.Objectives.OrderBy(o => o.SortOrder).ThenBy(o => o.Id).Select(o => new SurvivalObjectiveConfigDto
                {
                    Id = o.Id,
                    Name = o.Name,
                    InstaLose = o.InstantVictory,
                    GateStructureId = o.GateStructureId,
                    CaptureLocation = MapLocation(SiegeScenarioReadiness.CaptureLocationOf(o)),
                    TargetOverride = objectiveOverrides.GetValueOrDefault(o.Id, SurvivalTargetOverrideMode.Standard)
                }).ToList(),
                Gates = scenario.Gates.OrderBy(g => g.GateStructureId).Select(g => new SurvivalGateConfigDto
                {
                    GateStructureId = g.GateStructureId,
                    Name = g.GateStructure?.Name,
                    IsObjectiveGate = objectiveGateIds.Contains(g.GateStructureId),
                    TargetOverride = gateOverrides.GetValueOrDefault(g.GateStructureId, SurvivalTargetOverrideMode.Standard)
                }).ToList(),
                Stuck = new SurvivalStuckConfigDto
                {
                    StuckAfterMillis = settings.StuckAfterMillis,
                    LastEnemiesCount = settings.LastEnemiesCount,
                    RespawnLimit = settings.RespawnLimit,
                    RoundBackstopMillis = settings.RoundBackstopMillis,
                    MeaningfulDistance = settings.MeaningfulDistance
                },
                Spawn = new SurvivalSpawnConfigDto
                {
                    FavorNearPlayersPercent = settings.FavorNearPlayersPercent,
                    NearZoneCount = settings.NearZoneCount,
                    Zones = _mapper.Map<List<SurvivalSpawnZoneDto>>(graph.SpawnZones)
                },
                Difficulty = profile == null ? null : ToConfig(profile),
                DifficultyProfiles = graph.DifficultyProfiles.Select(ToConfig).ToList(),
                MonsterTypes = graph.MonsterTypes.Select(t => new SurvivalMonsterTypeConfigDto { EntityType = t.EntityType, Weight = t.Weight, UnlockRound = t.UnlockRound }).ToList(),
                SpecialWaves = new SurvivalSpecialWavesConfigDto
                {
                    Profiles = graph.SpecialWaveProfiles.Select(p => new SurvivalSpecialWaveProfileConfigDto
                    {
                        Id = p.Id,
                        Key = p.Key,
                        Name = p.Name,
                        CountOverride = p.CountOverride,
                        CountMultiplier = p.CountMultiplier,
                        Additive = p.Additive,
                        MonsterTypes = p.MonsterTypes.OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
                            .Select(t => new SurvivalMonsterTypeConfigDto { EntityType = t.EntityType, Weight = t.Weight, UnlockRound = t.UnlockRound }).ToList()
                    }).ToList(),
                    Entries = _mapper.Map<List<SurvivalSpecialWaveEntryDto>>(graph.SpecialWaveEntries)
                },
                KillRewards = new SurvivalKillRewardsConfigDto
                {
                    CoinsPerKill = settings.CoinsPerKill ?? scenario.CoinRewardCapture / 10m,
                    XpPerKill = settings.XpPerKill ?? scenario.ExpRewardCapture / 10m,
                    MatchCurrencyPerKill = settings.MatchCurrencyPerKill,
                    BonusMultiplier = settings.KillBonusMultiplier
                },
                PurchasePoints = _mapper.Map<List<SurvivalPurchasePointDto>>(graph.PurchasePoints),
                MysteryBoxPoints = graph.MysteryBoxPoints.Select(p => new SurvivalMysteryBoxPointConfigDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Location = MapLocation(p.Location),
                    InteractionRange = p.InteractionRange,
                    BlockType = p.BlockType,
                    RollPrice = p.RollPrice,
                    ClaimTimeoutMillis = p.ClaimTimeoutMillis,
                    Pool = _mapper.Map<SurvivalMysteryBoxPoolDto>(graph.MysteryBoxPools.FirstOrDefault(x => x.Id == p.SurvivalMysteryBoxPoolId)
                        ?? new SurvivalMysteryBoxPool { Id = p.SurvivalMysteryBoxPoolId, SiegeScenarioId = scenario.Id, Name = "?" })
                }).ToList(),
                Readiness = SiegeSurvivalReadiness.Evaluate(graph, lobbySettings, profile?.Id ?? difficultyProfileId)
            };
        }

        public async Task<SurvivalPreviewDto> PreviewAsync(int siegeScenarioId, int? difficultyProfileId, int fromRound, int toRound, int players)
        {
            var (_, _, profile) = await LoadForMatchAsync(siegeScenarioId, null, difficultyProfileId, requireSettings: false);
            if (profile == null) throw new ArgumentException("The scenario has no difficulty profile to preview.");
            return SurvivalScaling.Preview(profile, fromRound, toRound, players);
        }

        private async Task<(SurvivalScenarioGraph Graph, SurvivalLobbySettings? Lobby, SurvivalDifficultyProfile? Profile)> LoadForMatchAsync(
            int siegeScenarioId, int? siegeLobbyId, int? difficultyProfileId, bool requireSettings)
        {
            var scenario = await RequireScenarioAsync(siegeScenarioId);
            var graph = await _repo.GetGraphAsync(siegeScenarioId, scenario)
                ?? throw new KeyNotFoundException($"SiegeScenario with id {siegeScenarioId} not found.");
            if (requireSettings && graph.Settings == null)
                throw new KeyNotFoundException($"SiegeScenario {siegeScenarioId} has no survival settings.");

            SurvivalLobbySettings? lobbySettings = null;
            if (siegeLobbyId is > 0)
            {
                _ = await _repo.GetLobbyAsync(siegeLobbyId.Value) ?? throw new KeyNotFoundException($"SiegeLobby with id {siegeLobbyId} not found.");
                lobbySettings = await _repo.GetLobbySettingsAsync(siegeLobbyId.Value);
            }

            SurvivalDifficultyProfile? profile;
            if (difficultyProfileId is > 0)
            {
                profile = graph.DifficultyProfiles.FirstOrDefault(p => p.Id == difficultyProfileId.Value);
                if (profile == null && requireSettings)
                    throw new ArgumentException($"Difficulty profile {difficultyProfileId} is not a profile of scenario {siegeScenarioId}.");
            }
            else
            {
                profile = graph.DifficultyProfiles.OrderBy(p => p.SortOrder).ThenBy(p => p.Id).FirstOrDefault(p => p.IsDefault)
                    ?? graph.DifficultyProfiles.OrderBy(p => p.SortOrder).ThenBy(p => p.Id).FirstOrDefault();
            }
            return (graph, lobbySettings, profile);
        }

        private static SurvivalDifficultyConfigDto ToConfig(SurvivalDifficultyProfile p) => new()
        {
            Id = p.Id,
            Name = p.Name,
            IsDefault = p.IsDefault,
            CountCurve = new SurvivalScalingCurveDto { Preset = p.CountPreset, Base = p.CountBase, Step = p.CountStep, Multiplier = p.CountMultiplier, SwitchRound = p.CountSwitchRound },
            StatCurve = new SurvivalScalingCurveDto { Preset = p.StatPreset, Base = 1, Step = p.StatStep, Multiplier = p.StatMultiplier, SwitchRound = p.StatSwitchRound },
            StatWeights = new SurvivalStatWeightsDto
            {
                Health = p.HealthWeight, WalkingSpeed = p.WalkingSpeedWeight, MeleeDamage = p.MeleeDamageWeight,
                RangedDamage = p.RangedDamageWeight, AttackSpeed = p.AttackSpeedWeight
            },
            PerPlayer = new SurvivalPerPlayerScalingDto { Factor = p.PerPlayerFactor, ApplyToStats = p.PerPlayerAppliesToStats },
            MaxAlive = p.MaxAlive,
            StatCap = p.StatCap,
            CountCap = p.CountCap,
            RepairRatePerSecond = p.RepairRatePerSecond,
            GateDamageFactor = p.GateDamageFactor,
            CaptureContributionWeight = p.CaptureContributionWeight,
            SpawnIntervalMillis = p.SpawnIntervalMillis,
            AggroRadius = p.AggroRadius,
            LeashDistance = p.LeashDistance
        };

        private LocationDto? MapLocation(Location? location) => location == null ? null : _mapper.Map<LocationDto>(location);

        // ==== Child collections: one generic implementation, one Apply per entity ====

        private sealed class ChildOps<TEntity, TRead, TUpsert> : ISurvivalChildOperations<TRead, TUpsert> where TEntity : class, new()
        {
            private readonly SiegeSurvivalService _owner;
            private readonly Func<int, Expression<Func<TEntity, bool>>> _byParent;
            private readonly Func<int, Task<TEntity?>> _load;
            private readonly Func<TEntity, TUpsert, bool, Task> _apply;
            private readonly Func<TEntity, int> _order;
            private readonly Func<TEntity, Task>? _beforeDelete;

            public ChildOps(SiegeSurvivalService owner, Func<int, Expression<Func<TEntity, bool>>> byParent, Func<int, Task<TEntity?>> load,
                Func<TEntity, TUpsert, bool, Task> apply, Func<TEntity, int> order, Func<TEntity, Task>? beforeDelete = null)
            {
                _owner = owner;
                _byParent = byParent;
                _load = load;
                _apply = apply;
                _order = order;
                _beforeDelete = beforeDelete;
            }

            public async Task<List<TRead>> ListAsync(int parentId)
            {
                if (parentId <= 0) return new List<TRead>();
                var rows = await _owner._repo.ListAsync(_byParent(parentId));
                var loaded = new List<TEntity>();
                foreach (var row in rows.OrderBy(_order))
                    loaded.Add(await _load(IdOf(row)) ?? row);
                return _owner._mapper.Map<List<TRead>>(loaded);
            }

            public async Task<TRead?> GetAsync(int id)
            {
                if (id <= 0) return default;
                var row = await _load(id);
                return row == null ? default : _owner._mapper.Map<TRead>(row);
            }

            public async Task<TRead> CreateAsync(TUpsert dto)
            {
                if (dto == null) throw new ArgumentNullException(nameof(dto));
                var entity = new TEntity();
                await _apply(entity, dto, true);
                await _owner._repo.AddAsync(entity);
                var created = await _load(IdOf(entity));
                return _owner._mapper.Map<TRead>(created ?? entity);
            }

            public async Task UpdateAsync(int id, TUpsert dto)
            {
                if (dto == null) throw new ArgumentNullException(nameof(dto));
                if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
                var entity = await _load(id) ?? throw new KeyNotFoundException($"{typeof(TEntity).Name} with id {id} not found.");
                await _apply(entity, dto, false);
                await _owner._repo.SaveAsync(entity);
            }

            public async Task DeleteAsync(int id)
            {
                if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
                var entity = await _load(id) ?? throw new KeyNotFoundException($"{typeof(TEntity).Name} with id {id} not found.");
                if (_beforeDelete != null) await _beforeDelete(entity);
                await _owner._repo.RemoveAsync(entity);
            }

            private static int IdOf(TEntity entity) => (int)typeof(TEntity).GetProperty("Id")!.GetValue(entity)!;
        }

        // ---- Difficulty profiles ----

        private async Task ApplyDifficultyProfileAsync(SurvivalDifficultyProfile target, SurvivalDifficultyProfileUpsertDto dto, bool isCreate)
        {
            var scenarioId = await ParentAsync(isCreate, target.SiegeScenarioId, dto.SiegeScenarioId, "siegeScenarioId", "scenario");
            if (isCreate) await RequireScenarioRowAsync(scenarioId);
            target.Name = RequireText(dto.Name, "name", 100);
            if (!Enum.IsDefined(dto.CountPreset) || !Enum.IsDefined(dto.StatPreset))
                throw new ArgumentException("Presets must be Linear, Logarithmic, Exponential or Hybrid.");
            RequireFinite(dto.CountBase, "countBase");
            RequireFinite(dto.CountStep, "countStep");
            RequireFinite(dto.StatStep, "statStep");
            if (dto.CountBase < 1) throw new ArgumentException("countBase (the round-1 enemy count) must be at least 1.");
            if (!(dto.CountMultiplier > 0) || double.IsInfinity(dto.CountMultiplier)) throw new ArgumentException("countMultiplier must be above 0.");
            if (!(dto.StatMultiplier > 0) || double.IsInfinity(dto.StatMultiplier)) throw new ArgumentException("statMultiplier must be above 0.");
            if (dto.CountSwitchRound < 1 || dto.StatSwitchRound < 1) throw new ArgumentException("Switch rounds must be at least 1.");
            foreach (var (value, name) in new[]
                     {
                         (dto.HealthWeight, "healthWeight"), (dto.WalkingSpeedWeight, "walkingSpeedWeight"), (dto.MeleeDamageWeight, "meleeDamageWeight"),
                         (dto.RangedDamageWeight, "rangedDamageWeight"), (dto.AttackSpeedWeight, "attackSpeedWeight"), (dto.PerPlayerFactor, "perPlayerFactor"),
                         (dto.GateDamageFactor, "gateDamageFactor"), (dto.CaptureContributionWeight, "captureContributionWeight")
                     })
            {
                if (!(value >= 0) || double.IsInfinity(value)) throw new ArgumentException($"{name} can't be negative.");
            }
            if (dto.MaxAlive < 0) throw new ArgumentException("maxAlive can't be negative (0 = unlimited).");
            if (!(dto.StatCap >= 1) || double.IsInfinity(dto.StatCap)) throw new ArgumentException("statCap must be at least 1.");
            if (dto.CountCap < 0) throw new ArgumentException("countCap can't be negative (0 = unlimited).");
            if (!(dto.RepairRatePerSecond > 0) || double.IsInfinity(dto.RepairRatePerSecond)) throw new ArgumentException("repairRatePerSecond must be above 0.");
            if (dto.SpawnIntervalMillis < 0) throw new ArgumentException("spawnIntervalMillis can't be negative.");
            if (dto.AggroRadius is <= 0) throw new ArgumentException("aggroRadius must be above 0, or empty for the plugin's default.");
            if (dto.LeashDistance is <= 0) throw new ArgumentException("leashDistance must be above 0, or empty for the plugin's default.");

            var siblings = await _repo.ListAsync<SurvivalDifficultyProfile>(p => p.SiegeScenarioId == scenarioId);
            target.SiegeScenarioId = scenarioId;
            target.SortOrder = ResolveSortOrder(dto.SortOrder, target.SortOrder, isCreate, siblings.Select(p => p.SortOrder));
            // Only one default per scenario: marking this one clears the others.
            if (dto.IsDefault)
            {
                foreach (var other in siblings.Where(p => p.IsDefault && p.Id != target.Id))
                {
                    other.IsDefault = false;
                    await _repo.SaveAsync(other);
                }
            }
            target.IsDefault = dto.IsDefault || (isCreate && siblings.Count == 0);
            target.CountPreset = dto.CountPreset;
            target.CountBase = dto.CountBase;
            target.CountStep = dto.CountStep;
            target.CountMultiplier = dto.CountMultiplier;
            target.CountSwitchRound = dto.CountSwitchRound;
            target.StatPreset = dto.StatPreset;
            target.StatStep = dto.StatStep;
            target.StatMultiplier = dto.StatMultiplier;
            target.StatSwitchRound = dto.StatSwitchRound;
            target.HealthWeight = dto.HealthWeight;
            target.WalkingSpeedWeight = dto.WalkingSpeedWeight;
            target.MeleeDamageWeight = dto.MeleeDamageWeight;
            target.RangedDamageWeight = dto.RangedDamageWeight;
            target.AttackSpeedWeight = dto.AttackSpeedWeight;
            target.PerPlayerFactor = dto.PerPlayerFactor;
            target.PerPlayerAppliesToStats = dto.PerPlayerAppliesToStats;
            target.MaxAlive = dto.MaxAlive;
            target.StatCap = dto.StatCap;
            target.CountCap = dto.CountCap;
            target.RepairRatePerSecond = dto.RepairRatePerSecond;
            target.GateDamageFactor = dto.GateDamageFactor;
            target.CaptureContributionWeight = dto.CaptureContributionWeight;
            target.SpawnIntervalMillis = dto.SpawnIntervalMillis;
            target.AggroRadius = dto.AggroRadius;
            target.LeashDistance = dto.LeashDistance;
        }

        // ---- Monster types ----

        private async Task ApplyMonsterTypeAsync(SurvivalMonsterType target, SurvivalMonsterTypeUpsertDto dto, bool isCreate)
        {
            var scenarioId = await ParentAsync(isCreate, target.SiegeScenarioId, dto.SiegeScenarioId, "siegeScenarioId", "scenario");
            if (isCreate) await RequireScenarioRowAsync(scenarioId);
            var entityType = NormalizeEntityType(dto.EntityType);
            var siblings = await _repo.ListAsync<SurvivalMonsterType>(m => m.SiegeScenarioId == scenarioId);
            if (siblings.Any(m => m.Id != target.Id && m.EntityType == entityType))
                throw new ArgumentException($"Monster type {entityType} is already selected for this scenario.");
            ValidateMix(dto.Weight, dto.UnlockRound);

            target.SiegeScenarioId = scenarioId;
            target.EntityType = entityType;
            target.Weight = dto.Weight;
            target.UnlockRound = dto.UnlockRound;
            target.SortOrder = ResolveSortOrder(dto.SortOrder, target.SortOrder, isCreate, siblings.Select(m => m.SortOrder));
        }

        private async Task ApplySpecialWaveMonsterTypeAsync(SurvivalSpecialWaveMonsterType target, SurvivalSpecialWaveMonsterTypeUpsertDto dto, bool isCreate)
        {
            var profileId = await ParentAsync(isCreate, target.SurvivalSpecialWaveProfileId, dto.SurvivalSpecialWaveProfileId, "survivalSpecialWaveProfileId", "special-wave profile");
            if (isCreate && await _repo.FindAsync<SurvivalSpecialWaveProfile>(profileId) == null)
                throw new KeyNotFoundException($"SurvivalSpecialWaveProfile with id {profileId} not found.");
            var entityType = NormalizeEntityType(dto.EntityType);
            var siblings = await _repo.ListAsync<SurvivalSpecialWaveMonsterType>(m => m.SurvivalSpecialWaveProfileId == profileId);
            if (siblings.Any(m => m.Id != target.Id && m.EntityType == entityType))
                throw new ArgumentException($"Monster type {entityType} is already in this profile.");
            ValidateMix(dto.Weight, dto.UnlockRound);

            target.SurvivalSpecialWaveProfileId = profileId;
            target.EntityType = entityType;
            target.Weight = dto.Weight;
            target.UnlockRound = dto.UnlockRound;
            target.SortOrder = ResolveSortOrder(dto.SortOrder, target.SortOrder, isCreate, siblings.Select(m => m.SortOrder));
        }

        private static void ValidateMix(double weight, int unlockRound)
        {
            if (!(weight > 0) || double.IsInfinity(weight)) throw new ArgumentException("weight must be above 0.");
            if (unlockRound < 1) throw new ArgumentException("unlockRound must be at least 1.");
        }

        private static string NormalizeEntityType(string? value)
        {
            var entityType = value?.Trim().ToUpperInvariant() ?? string.Empty;
            if (entityType.StartsWith("MINECRAFT:", StringComparison.Ordinal)) entityType = entityType["MINECRAFT:".Length..];
            if (!EntityTypePattern.IsMatch(entityType))
                throw new ArgumentException("entityType must be a vanilla entity type key such as ZOMBIE or cave_spider.");
            return entityType;
        }

        // ---- Special waves ----

        private async Task ApplySpecialWaveProfileAsync(SurvivalSpecialWaveProfile target, SurvivalSpecialWaveProfileUpsertDto dto, bool isCreate)
        {
            var scenarioId = await ParentAsync(isCreate, target.SiegeScenarioId, dto.SiegeScenarioId, "siegeScenarioId", "scenario");
            if (isCreate) await RequireScenarioRowAsync(scenarioId);
            var key = dto.Key?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!ProfileKeyPattern.IsMatch(key)) throw new ArgumentException("key must be 1–64 lowercase letters, digits, '-' or '_'.");
            var siblings = await _repo.ListAsync<SurvivalSpecialWaveProfile>(p => p.SiegeScenarioId == scenarioId);
            if (siblings.Any(p => p.Id != target.Id && p.Key == key)) throw new ArgumentException($"Special-wave profile key '{key}' is already used in this scenario.");
            if (dto.CountOverride is < 1) throw new ArgumentException("countOverride must be at least 1, or empty.");
            if (!(dto.CountMultiplier > 0) || double.IsInfinity(dto.CountMultiplier)) throw new ArgumentException("countMultiplier must be above 0.");

            target.SiegeScenarioId = scenarioId;
            target.Key = key;
            target.Name = RequireText(dto.Name, "name", 100);
            target.CountOverride = dto.CountOverride;
            target.CountMultiplier = dto.CountMultiplier;
            target.Additive = dto.Additive;
            target.SortOrder = ResolveSortOrder(dto.SortOrder, target.SortOrder, isCreate, siblings.Select(p => p.SortOrder));
        }

        private async Task ApplySpecialWaveEntryAsync(SurvivalSpecialWaveEntry target, SurvivalSpecialWaveEntryUpsertDto dto, bool isCreate)
        {
            var scenarioId = await ParentAsync(isCreate, target.SiegeScenarioId, dto.SiegeScenarioId, "siegeScenarioId", "scenario");
            if (isCreate) await RequireScenarioRowAsync(scenarioId);
            if (dto.SurvivalSpecialWaveProfileId <= 0) throw new ArgumentException("survivalSpecialWaveProfileId is required.");
            var profile = await _repo.FindAsync<SurvivalSpecialWaveProfile>(dto.SurvivalSpecialWaveProfileId)
                ?? throw new ArgumentException($"SurvivalSpecialWaveProfile with id {dto.SurvivalSpecialWaveProfileId} not found.");
            if (profile.SiegeScenarioId != scenarioId) throw new ArgumentException("The special-wave profile must belong to the same scenario.");
            if (!Enum.IsDefined(dto.Kind)) throw new ArgumentException("kind must be Recurring or Explicit.");

            if (dto.Kind == SurvivalSpecialWaveKind.Recurring)
            {
                if (dto.Interval is null or < 1) throw new ArgumentException("A recurring entry needs an interval of 1 or more.");
                if (dto.StartWave is < 1) throw new ArgumentException("startWave must be at least 1, or empty for the interval.");
                target.Interval = dto.Interval;
                target.StartWave = dto.StartWave;
                target.Wave = null;
            }
            else
            {
                if (dto.Wave is null or < 1) throw new ArgumentException("An explicit entry needs a wave number of 1 or more.");
                target.Wave = dto.Wave;
                target.Interval = null;
                target.StartWave = null;
            }

            var siblings = await _repo.ListAsync<SurvivalSpecialWaveEntry>(e => e.SiegeScenarioId == scenarioId);
            target.SiegeScenarioId = scenarioId;
            target.SurvivalSpecialWaveProfileId = profile.Id;
            target.Kind = dto.Kind;
            target.Priority = dto.Priority;
            target.SortOrder = ResolveSortOrder(dto.SortOrder, target.SortOrder, isCreate, siblings.Select(e => e.SortOrder));
        }

        // ---- Spawnzones ----

        private async Task ApplySpawnZoneAsync(SurvivalSpawnZone target, SurvivalSpawnZoneUpsertDto dto, bool isCreate)
        {
            var scenarioId = await ParentAsync(isCreate, target.SiegeScenarioId, dto.SiegeScenarioId, "siegeScenarioId", "scenario");
            if (isCreate) await RequireScenarioRowAsync(scenarioId);
            var name = RequireText(dto.Name, "name", 100);
            if (!(dto.Weight > 0) || double.IsInfinity(dto.Weight)) throw new ArgumentException("weight must be above 0.");
            var anchorId = await ResolveLocationAsync(dto.AnchorLocationId, dto.AnchorLocation, "spawnzone anchor")
                ?? throw new ArgumentException("A spawnzone needs an anchor location (capture it in-game).");
            if (dto.AreaRadius is <= 0 || (dto.AreaRadius.HasValue && double.IsInfinity(dto.AreaRadius.Value)))
                throw new ArgumentException("areaRadius must be above 0, or empty to spawn only at the zone's spawnpoints.");
            if (!Enum.IsDefined(dto.PrerequisitePolicy)) throw new ArgumentException("prerequisitePolicy must be All or Any.");
            if (dto.MinWave < 1) throw new ArgumentException("minWave must be at least 1.");
            if (dto.MaxWave.HasValue && dto.MaxWave.Value < dto.MinWave) throw new ArgumentException("maxWave must be at least minWave, or empty.");

            List<int>? prerequisites = null;
            if (dto.PrerequisiteObjectiveIds != null)
            {
                prerequisites = dto.PrerequisiteObjectiveIds.Where(id => id > 0).Distinct().ToList();
                if (prerequisites.Count > 0)
                {
                    var objectives = await _repo.ListAsync<SiegeObjective>(o => o.SiegeScenarioId == scenarioId);
                    var foreign = prerequisites.FirstOrDefault(id => objectives.All(o => o.Id != id));
                    if (foreign != 0) throw new ArgumentException($"Objective {foreign} is not an objective of this scenario.");
                }
            }

            var siblings = await _repo.ListAsync<SurvivalSpawnZone>(z => z.SiegeScenarioId == scenarioId);
            target.SiegeScenarioId = scenarioId;
            target.Name = name;
            target.Weight = dto.Weight;
            target.AnchorLocationId = anchorId;
            target.AreaRadius = dto.AreaRadius;
            target.PrerequisitePolicy = dto.PrerequisitePolicy;
            target.MinWave = dto.MinWave;
            target.MaxWave = dto.MaxWave;
            target.ForNormalWaves = dto.ForNormalWaves;
            target.ForSpecialWaves = dto.ForSpecialWaves;
            target.LaneAutoReroute = dto.LaneAutoReroute;
            target.SortOrder = ResolveSortOrder(dto.SortOrder, target.SortOrder, isCreate, siblings.Select(z => z.SortOrder));
            if (prerequisites != null)
            {
                foreach (var stale in target.PrerequisiteObjectives.Where(p => !prerequisites.Contains(p.SiegeObjectiveId)).ToList())
                    target.PrerequisiteObjectives.Remove(stale);
                foreach (var id in prerequisites.Where(id => target.PrerequisiteObjectives.All(p => p.SiegeObjectiveId != id)))
                    target.PrerequisiteObjectives.Add(new SurvivalSpawnZoneObjective { SurvivalSpawnZoneId = target.Id, SiegeObjectiveId = id });
            }
        }

        private async Task ApplySpawnZonePointAsync(SurvivalSpawnZonePoint target, SurvivalSpawnZonePointUpsertDto dto, bool isCreate)
        {
            var zoneId = await ParentAsync(isCreate, target.SurvivalSpawnZoneId, dto.SurvivalSpawnZoneId, "survivalSpawnZoneId", "spawnzone");
            if (isCreate && await _repo.FindAsync<SurvivalSpawnZone>(zoneId) == null)
                throw new KeyNotFoundException($"SurvivalSpawnZone with id {zoneId} not found.");
            if (!Enum.IsDefined(dto.Kind)) throw new ArgumentException("kind must be Spawn or Lane.");
            var locationId = await ResolveLocationAsync(dto.LocationId, dto.Location, "spawnzone point")
                ?? throw new ArgumentException("A spawnzone point needs a location (capture it in-game).");
            var siblings = await _repo.ListAsync<SurvivalSpawnZonePoint>(p => p.SurvivalSpawnZoneId == zoneId && p.Kind == dto.Kind);

            target.SurvivalSpawnZoneId = zoneId;
            target.Kind = dto.Kind;
            target.Name = OptionalText(dto.Name, "name", 100);
            target.LocationId = locationId;
            target.SortOrder = ResolveSortOrder(dto.SortOrder, target.SortOrder, isCreate, siblings.Where(p => p.Id != target.Id).Select(p => p.SortOrder));
        }

        // ---- Target overrides ----

        private async Task ApplyTargetOverrideAsync(SurvivalTargetOverride target, SurvivalTargetOverrideUpsertDto dto, bool isCreate)
        {
            var scenarioId = await ParentAsync(isCreate, target.SiegeScenarioId, dto.SiegeScenarioId, "siegeScenarioId", "scenario");
            if (isCreate) await RequireScenarioRowAsync(scenarioId);
            int? objectiveId = dto.SiegeObjectiveId is > 0 ? dto.SiegeObjectiveId : null;
            int? gateId = dto.GateStructureId is > 0 ? dto.GateStructureId : null;
            if (objectiveId.HasValue == gateId.HasValue) throw new ArgumentException("A target override names exactly one objective or one gate.");
            if (!Enum.IsDefined(dto.Mode)) throw new ArgumentException("mode must be Standard, Allow or Deny.");

            if (objectiveId.HasValue)
            {
                var objective = await _repo.FindAsync<SiegeObjective>(objectiveId.Value);
                if (objective == null || objective.SiegeScenarioId != scenarioId) throw new ArgumentException("The objective must be an objective of this scenario.");
            }
            else
            {
                var gates = await _repo.ListAsync<SiegeScenarioGate>(g => g.SiegeScenarioId == scenarioId && g.GateStructureId == gateId!.Value);
                if (gates.Count == 0) throw new ArgumentException("The gate must be one of the scenario's selected gates.");
            }
            var siblings = await _repo.ListAsync<SurvivalTargetOverride>(o => o.SiegeScenarioId == scenarioId);
            if (siblings.Any(o => o.Id != target.Id && o.SiegeObjectiveId == objectiveId && o.GateStructureId == gateId))
                throw new ArgumentException("This target already has an override; edit that one instead.");

            target.SiegeScenarioId = scenarioId;
            target.SiegeObjectiveId = objectiveId;
            target.GateStructureId = gateId;
            target.Mode = dto.Mode;
        }

        // ---- Purchase points ----

        private async Task ApplyPurchasePointAsync(SurvivalPurchasePoint target, SurvivalPurchasePointUpsertDto dto, bool isCreate)
        {
            var scenarioId = await ParentAsync(isCreate, target.SiegeScenarioId, dto.SiegeScenarioId, "siegeScenarioId", "scenario");
            if (isCreate) await RequireScenarioRowAsync(scenarioId);
            var name = RequireText(dto.Name, "name", 100);
            var locationId = await ResolveLocationAsync(dto.LocationId, dto.Location, "purchase point")
                ?? throw new ArgumentException("A purchase point needs a location (capture it in-game).");
            RequireRange(dto.InteractionRange);
            var siblings = await _repo.ListAsync<SurvivalPurchasePoint>(p => p.SiegeScenarioId == scenarioId);

            target.SiegeScenarioId = scenarioId;
            target.Name = name;
            target.LocationId = locationId;
            target.InteractionRange = dto.InteractionRange;
            target.SortOrder = ResolveSortOrder(dto.SortOrder, target.SortOrder, isCreate, siblings.Select(p => p.SortOrder));
        }

        private async Task ApplyPurchaseOfferAsync(SurvivalPurchaseOffer target, SurvivalPurchaseOfferUpsertDto dto, bool isCreate)
        {
            var pointId = await ParentAsync(isCreate, target.SurvivalPurchasePointId, dto.SurvivalPurchasePointId, "survivalPurchasePointId", "purchase point");
            if (isCreate && await _repo.FindAsync<SurvivalPurchasePoint>(pointId) == null)
                throw new KeyNotFoundException($"SurvivalPurchasePoint with id {pointId} not found.");
            if (dto.ItemBlueprintId <= 0 || !(await _repo.ExistingItemBlueprintIdsAsync(new[] { dto.ItemBlueprintId })).Contains(dto.ItemBlueprintId))
                throw new ArgumentException($"ItemBlueprint with id {dto.ItemBlueprintId} not found.");
            if (dto.Quantity < 1) throw new ArgumentException("quantity must be at least 1.");
            if (dto.Price < 0) throw new ArgumentException("price can't be negative.");
            var siblings = await _repo.ListAsync<SurvivalPurchaseOffer>(o => o.SurvivalPurchasePointId == pointId);

            target.SurvivalPurchasePointId = pointId;
            target.ItemBlueprintId = dto.ItemBlueprintId;
            target.Quantity = dto.Quantity;
            target.Price = dto.Price;
            target.SortOrder = ResolveSortOrder(dto.SortOrder, target.SortOrder, isCreate, siblings.Select(o => o.SortOrder));
        }

        // ---- Mystery Box ----

        private async Task ApplyMysteryBoxPoolAsync(SurvivalMysteryBoxPool target, SurvivalMysteryBoxPoolUpsertDto dto, bool isCreate)
        {
            var scenarioId = await ParentAsync(isCreate, target.SiegeScenarioId, dto.SiegeScenarioId, "siegeScenarioId", "scenario");
            if (isCreate) await RequireScenarioRowAsync(scenarioId);
            if (!Enum.IsDefined(dto.Variant)) throw new ArgumentException("variant must be Items or EnchantedBooks.");
            target.SiegeScenarioId = scenarioId;
            target.Name = RequireText(dto.Name, "name", 100);
            target.Variant = dto.Variant;
        }

        private async Task GuardPoolDeleteAsync(SurvivalMysteryBoxPool pool)
        {
            if (await _repo.MysteryBoxPoolInUseAsync(pool.Id))
                throw new InvalidOperationException("This pool is used by a Mystery Box point; point it at another pool or delete the point first.");
        }

        private async Task ApplyMysteryBoxEntryAsync(SurvivalMysteryBoxEntry target, SurvivalMysteryBoxEntryUpsertDto dto, bool isCreate)
        {
            var poolId = await ParentAsync(isCreate, target.SurvivalMysteryBoxPoolId, dto.SurvivalMysteryBoxPoolId, "survivalMysteryBoxPoolId", "pool");
            var pool = await _repo.FindAsync<SurvivalMysteryBoxPool>(poolId)
                ?? throw new KeyNotFoundException($"SurvivalMysteryBoxPool with id {poolId} not found.");
            var chance = RequireChance(dto.ChancePercent, "chancePercent");

            int? blueprintId = dto.ItemBlueprintId is > 0 ? dto.ItemBlueprintId : null;
            int? enchantmentId = dto.EnchantmentDefinitionId is > 0 ? dto.EnchantmentDefinitionId : null;
            List<SurvivalMysteryBoxEntryEnchantmentUpsertDto>? enchantments = null;
            if (pool.Variant == SurvivalMysteryBoxVariant.Items)
            {
                if (!blueprintId.HasValue) throw new ArgumentException("An entry of an items pool needs an itemBlueprintId.");
                if (!(await _repo.ExistingItemBlueprintIdsAsync(new[] { blueprintId.Value })).Contains(blueprintId.Value))
                    throw new ArgumentException($"ItemBlueprint with id {blueprintId} not found.");
                enchantmentId = null;
                if (dto.Enchantments != null)
                {
                    enchantments = dto.Enchantments.Where(e => e != null).ToList();
                    var duplicate = enchantments.GroupBy(e => e.EnchantmentDefinitionId).FirstOrDefault(g => g.Count() > 1);
                    if (duplicate != null) throw new ArgumentException($"Enchantment {duplicate.Key} is listed twice on this entry.");
                    var ids = enchantments.Select(e => e.EnchantmentDefinitionId).ToList();
                    var existing = await _repo.ExistingEnchantmentDefinitionIdsAsync(ids);
                    foreach (var e in enchantments)
                    {
                        if (e.EnchantmentDefinitionId <= 0 || !existing.Contains(e.EnchantmentDefinitionId))
                            throw new ArgumentException($"EnchantmentDefinition with id {e.EnchantmentDefinitionId} not found.");
                        if (e.Level < 1) throw new ArgumentException("An enchantment level must be at least 1.");
                        RequireChance(e.ChancePercent, "enchantment chancePercent");
                    }
                }
            }
            else
            {
                if (!enchantmentId.HasValue) throw new ArgumentException("An entry of an enchanted-books pool needs an enchantmentDefinitionId.");
                if (!(await _repo.ExistingEnchantmentDefinitionIdsAsync(new[] { enchantmentId.Value })).Contains(enchantmentId.Value))
                    throw new ArgumentException($"EnchantmentDefinition with id {enchantmentId} not found.");
                if (dto.EnchantmentLevel is null or < 1) throw new ArgumentException("An enchanted-book entry needs an enchantmentLevel of 1 or more.");
                blueprintId = null;
                enchantments = new List<SurvivalMysteryBoxEntryEnchantmentUpsertDto>();
            }

            var siblings = await _repo.ListAsync<SurvivalMysteryBoxEntry>(e => e.SurvivalMysteryBoxPoolId == poolId);
            target.SurvivalMysteryBoxPoolId = poolId;
            target.ItemBlueprintId = blueprintId;
            target.EnchantmentDefinitionId = enchantmentId;
            target.EnchantmentLevel = enchantmentId.HasValue ? dto.EnchantmentLevel : null;
            target.ChancePercent = chance;
            target.SortOrder = ResolveSortOrder(dto.SortOrder, target.SortOrder, isCreate, siblings.Select(e => e.SortOrder));
            if (enchantments != null)
            {
                var wanted = enchantments.Select(e => e.EnchantmentDefinitionId).ToHashSet();
                foreach (var stale in target.Enchantments.Where(e => !wanted.Contains(e.EnchantmentDefinitionId)).ToList())
                    target.Enchantments.Remove(stale);
                foreach (var e in enchantments)
                {
                    var row = target.Enchantments.FirstOrDefault(x => x.EnchantmentDefinitionId == e.EnchantmentDefinitionId);
                    if (row == null)
                    {
                        row = new SurvivalMysteryBoxEntryEnchantment { SurvivalMysteryBoxEntryId = target.Id, EnchantmentDefinitionId = e.EnchantmentDefinitionId };
                        target.Enchantments.Add(row);
                    }
                    row.Level = e.Level;
                    row.ChancePercent = e.ChancePercent;
                }
            }
        }

        private async Task ApplyMysteryBoxPointAsync(SurvivalMysteryBoxPoint target, SurvivalMysteryBoxPointUpsertDto dto, bool isCreate)
        {
            var scenarioId = await ParentAsync(isCreate, target.SiegeScenarioId, dto.SiegeScenarioId, "siegeScenarioId", "scenario");
            if (isCreate) await RequireScenarioRowAsync(scenarioId);
            var name = RequireText(dto.Name, "name", 100);
            var locationId = await ResolveLocationAsync(dto.LocationId, dto.Location, "Mystery Box")
                ?? throw new ArgumentException("A Mystery Box needs a location (capture it in-game).");
            RequireRange(dto.InteractionRange);
            if (dto.SurvivalMysteryBoxPoolId <= 0) throw new ArgumentException("survivalMysteryBoxPoolId is required.");
            var pool = await _repo.FindAsync<SurvivalMysteryBoxPool>(dto.SurvivalMysteryBoxPoolId)
                ?? throw new ArgumentException($"SurvivalMysteryBoxPool with id {dto.SurvivalMysteryBoxPoolId} not found.");
            if (pool.SiegeScenarioId != scenarioId) throw new ArgumentException("The pool must belong to the same scenario.");
            var blockType = dto.BlockType?.Trim().ToLowerInvariant() ?? string.Empty;
            if (blockType.Length > 0 && !blockType.Contains(':')) blockType = "minecraft:" + blockType;
            if (!BlockKeyPattern.IsMatch(blockType) || blockType.Length > 191)
                throw new ArgumentException("blockType must be a block key such as minecraft:end_portal_frame.");
            if (dto.RollPrice < 0) throw new ArgumentException("rollPrice can't be negative.");
            if (dto.ClaimTimeoutMillis < 0) throw new ArgumentException("claimTimeoutMillis can't be negative.");
            var siblings = await _repo.ListAsync<SurvivalMysteryBoxPoint>(p => p.SiegeScenarioId == scenarioId);

            target.SiegeScenarioId = scenarioId;
            target.Name = name;
            target.LocationId = locationId;
            target.InteractionRange = dto.InteractionRange;
            target.SurvivalMysteryBoxPoolId = pool.Id;
            target.BlockType = blockType;
            target.RollPrice = dto.RollPrice;
            target.ClaimTimeoutMillis = dto.ClaimTimeoutMillis;
            target.SortOrder = ResolveSortOrder(dto.SortOrder, target.SortOrder, isCreate, siblings.Select(p => p.SortOrder));
        }

        // ==== Helpers ====

        private async Task<SiegeScenario> RequireScenarioAsync(int siegeScenarioId)
        {
            if (siegeScenarioId <= 0) throw new KeyNotFoundException($"SiegeScenario with id {siegeScenarioId} not found.");
            return await _scenarioRepo.GetByIdAsync(siegeScenarioId)
                ?? throw new KeyNotFoundException($"SiegeScenario with id {siegeScenarioId} not found.");
        }

        private async Task RequireScenarioRowAsync(int siegeScenarioId)
        {
            if (siegeScenarioId <= 0 || await _repo.FindAsync<SiegeScenario>(siegeScenarioId) == null)
                throw new KeyNotFoundException($"SiegeScenario with id {siegeScenarioId} not found.");
        }

        // The parent of a child row: required on create; on update it can't change (delete and re-add instead).
        private static Task<int> ParentAsync(bool isCreate, int current, int fromDto, string field, string parentName)
        {
            if (isCreate)
            {
                if (fromDto <= 0) throw new ArgumentException($"{field} is required.");
                return Task.FromResult(fromDto);
            }
            if (fromDto > 0 && fromDto != current)
                throw new ArgumentException($"A row can't be moved to another {parentName}; delete it and add it there instead.");
            return Task.FromResult(current);
        }

        private static int ResolveSortOrder(int? requested, int current, bool isCreate, IEnumerable<int> siblings)
        {
            if (requested.HasValue)
            {
                if (requested.Value < 0) throw new ArgumentException("sortOrder can't be negative.");
                return requested.Value;
            }
            if (!isCreate) return current;
            var list = siblings.ToList();
            return list.Count == 0 ? 0 : list.Max() + 1;
        }

        private static decimal RequireChance(decimal percent, string field)
        {
            try
            {
                var bp = SurvivalScaling.ToBasisPoints(percent);
                if (bp < 1) throw new ArgumentException($"{field} must be at least 0.01 %.");
                return percent;
            }
            catch (ArgumentException ex) when (!ex.Message.StartsWith(field, StringComparison.Ordinal))
            {
                throw new ArgumentException($"{field}: {ex.Message}");
            }
        }

        private static void RequireRange(double range)
        {
            if (!(range > 0) || range > 64) throw new ArgumentException("interactionRange must be above 0 and at most 64.");
        }

        private static void RequireFinite(double value, string field)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException($"{field} must be a number.");
        }

        // GateStructureService's / SiegeScenarioService's world-bound location convention: an id of an existing
        // Location, or an inline location (created when it has no id, updated when it has one).
        private async Task<int?> ResolveLocationAsync(int? locationId, LocationDto? locationDto, string fieldName)
        {
            int? id = locationId is > 0 ? locationId : null;
            if (locationDto == null)
            {
                if (!id.HasValue) return null;
                if (!await _repo.LocationExistsAsync(id.Value))
                    throw new ArgumentException($"Location with id {id} not found for the {fieldName}.");
                return id;
            }

            if (id.HasValue && locationDto.Id is > 0 && locationDto.Id.Value != id.Value)
                throw new ArgumentException($"Conflicting location references for the {fieldName}: locationId={id}, location.id={locationDto.Id}.");

            if (locationDto.Id is not > 0)
            {
                var created = await _locationService.CreateAsync(locationDto);
                return created.Id;
            }

            await _locationService.UpdateAsync(locationDto.Id.Value, locationDto);
            return locationDto.Id.Value;
        }

        private static string RequireText(string? value, string field, int maxLength)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed)) throw new ArgumentException($"{field} is required.");
            if (trimmed.Length > maxLength) throw new ArgumentException($"{field} can be at most {maxLength} characters.");
            return trimmed;
        }

        private static string? OptionalText(string? value, string field, int maxLength)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed)) return null;
            if (trimmed.Length > maxLength) throw new ArgumentException($"{field} can be at most {maxLength} characters.");
            return trimmed;
        }
    }
}
