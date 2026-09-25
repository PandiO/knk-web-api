using System.Text.RegularExpressions;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    // Siege Phase 2 (docs/specs/siege-minigame/DESIGN.md §3.8, D1, §11.2): lobby CRUD with its
    // weighted rotation, and the plugin's runtime-config payload. Same error convention as the other
    // siege services (ArgumentException -> 400, KeyNotFoundException -> 404,
    // InvalidOperationException -> 409).
    public class SiegeLobbyService : ISiegeLobbyService
    {
        // /siege join <key> - one lowercase token.
        private static readonly Regex KeyPattern = new("^[a-z0-9_-]{1,64}$", RegexOptions.Compiled);

        private readonly ISiegeLobbyRepository _repo;
        private readonly ISiegeScenarioRepository _scenarioRepo;
        private readonly ISiegeConfigurationService _configurationService;
        private readonly IMapper _mapper;

        public SiegeLobbyService(
            ISiegeLobbyRepository repo,
            ISiegeScenarioRepository scenarioRepo,
            ISiegeConfigurationService configurationService,
            IMapper mapper)
        {
            _repo = repo;
            _scenarioRepo = scenarioRepo;
            _configurationService = configurationService;
            _mapper = mapper;
        }

        public async Task<IEnumerable<SiegeLobbyReadDto>> GetAllAsync()
        {
            return _mapper.Map<IEnumerable<SiegeLobbyReadDto>>(await _repo.GetAllAsync());
        }

        public async Task<SiegeLobbyReadDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var entity = await _repo.GetByIdAsync(id);
            return entity == null ? null : _mapper.Map<SiegeLobbyReadDto>(entity);
        }

        public async Task<SiegeLobbyReadDto> CreateAsync(SiegeLobbyUpsertDto dto)
        {
            var entity = new SiegeLobby();
            await ApplyAsync(entity, dto, existingId: null);
            await _repo.AddAsync(entity);

            var created = await _repo.GetByIdAsync(entity.Id);
            return _mapper.Map<SiegeLobbyReadDto>(created ?? entity);
        }

        public async Task UpdateAsync(int id, SiegeLobbyUpsertDto dto)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeLobby with id {id} not found.");
            await ApplyAsync(existing, dto, existingId: id);
            await _repo.UpdateAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeLobby with id {id} not found.");
            if (await _repo.HasMatchHistoryAsync(id))
                throw new InvalidOperationException("This lobby has match history and can't be deleted. Disable it instead.");
            await _repo.DeleteAsync(existing);
        }

        public async Task<PagedResultDto<SiegeLobbyListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));
            var result = await _repo.SearchAsync(_mapper.Map<PagedQuery>(queryDto));
            return _mapper.Map<PagedResultDto<SiegeLobbyListDto>>(result);
        }

        // ==== Runtime config (GET /api/siege-lobbies/runtime-config) ====

        // Everything the plugin caches between matches: the global configuration, enabled lobbies,
        // and their rotation scenarios fully resolved. Only READY scenarios enter a rotation (§3.3);
        // the rest are reported per lobby under SkippedScenarios. Readiness here is the structural
        // §3.9 rule set only - the WorldGuard-region checks call back into the plugin, which is the
        // caller of this endpoint (possibly while it is still starting), so they stay an authoring-
        // time check on GET …/readiness.
        public async Task<SiegeRuntimeConfigDto> GetRuntimeConfigAsync()
        {
            var result = new SiegeRuntimeConfigDto
            {
                GeneratedAt = DateTime.UtcNow,
                Configuration = await _configurationService.GetAsync()
            };

            var lobbies = await _repo.GetEnabledAsync();
            var scenarioIds = lobbies.SelectMany(l => l.Rotation).Select(r => r.SiegeScenarioId).Distinct().ToList();
            var scenarios = (await _scenarioRepo.GetByIdsAsync(scenarioIds)).ToDictionary(s => s.Id);

            // Evaluate/resolve each scenario once, even when several lobbies rotate it.
            var readiness = scenarios.Values.ToDictionary(s => s.Id, SiegeScenarioReadiness.EvaluateStructure);
            var resolved = new Dictionary<int, SiegeRuntimeScenarioDto>();

            foreach (var lobby in lobbies)
            {
                var lobbyDto = new SiegeRuntimeLobbyDto
                {
                    Id = lobby.Id,
                    Name = lobby.Name,
                    Key = lobby.Key,
                    Mode = lobby.Mode,
                    MatchmakingSeconds = lobby.MatchmakingSeconds,
                    CooldownSeconds = lobby.CooldownSeconds,
                    VoteCandidateCount = lobby.VoteCandidateCount,
                    AllowRandomVote = lobby.AllowRandomVote
                };

                foreach (var entry in lobby.Rotation.OrderBy(r => r.SiegeScenarioId))
                {
                    if (!scenarios.TryGetValue(entry.SiegeScenarioId, out var scenario)) continue;

                    var check = readiness[scenario.Id];
                    if (!check.IsReady)
                    {
                        lobbyDto.SkippedScenarios.Add(new SiegeRuntimeSkippedScenarioDto
                        {
                            SiegeScenarioId = scenario.Id,
                            Name = scenario.Name,
                            Errors = check.Errors
                        });
                        continue;
                    }

                    if (!resolved.TryGetValue(scenario.Id, out var scenarioDto))
                    {
                        scenarioDto = BuildRuntimeScenario(scenario);
                        resolved[scenario.Id] = scenarioDto;
                    }
                    lobbyDto.Rotation.Add(new SiegeRuntimeRotationEntryDto { Weight = entry.Weight, Scenario = scenarioDto });
                }

                result.Lobbies.Add(lobbyDto);
            }

            return result;
        }

        // A ready scenario with every default resolved, so the plugin never re-implements them:
        // team identity from the Clan, "null -> first Defender team" for objective holders and gate
        // owners, and objective capture points from the gate when the objective has no location.
        private SiegeRuntimeScenarioDto BuildRuntimeScenario(SiegeScenario scenario)
        {
            var teams = scenario.Teams.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToList();
            // Ready implies a Defender exists (DEFENDER_REQUIRED).
            var firstDefenderId = SiegeTeamIdentity.FirstDefender(teams)!.Id;
            var objectiveGateIds = scenario.Objectives
                .Where(o => o.GateStructureId.HasValue).Select(o => o.GateStructureId!.Value).ToHashSet();

            return new SiegeRuntimeScenarioDto
            {
                Id = scenario.Id,
                Name = scenario.Name,
                Description = scenario.Description,
                TownId = scenario.TownId,
                TownName = scenario.Town?.Name,
                TownWgRegionId = scenario.Town?.WgRegionId,
                Districts = scenario.Districts
                    .OrderBy(d => d.DistrictId)
                    .Select(d => new SiegeRuntimeDistrictDto { Id = d.DistrictId, Name = d.District?.Name, WgRegionId = d.District?.WgRegionId })
                    .ToList(),
                HubLocation = MapLocation(scenario.HubLocation),
                PlayersMin = scenario.PlayersMin,
                PlayersMax = scenario.PlayersMax,
                MinTitleBracketId = scenario.MinTitleBracketId,
                MinTitleExperience = scenario.MinTitleBracket?.MinExperience,
                MatchDurationMinSeconds = scenario.MatchDurationMinSeconds,
                MatchDurationPerPlayerSeconds = scenario.MatchDurationPerPlayerSeconds,
                MatchDurationMaxSeconds = scenario.MatchDurationMaxSeconds,
                CoinRewardWin = scenario.CoinRewardWin,
                ExpRewardWin = scenario.ExpRewardWin,
                GemRewardWin = scenario.GemRewardWin,
                CoinRewardHolding = scenario.CoinRewardHolding,
                ExpRewardHolding = scenario.ExpRewardHolding,
                CoinRewardCapture = scenario.CoinRewardCapture,
                ExpRewardCapture = scenario.ExpRewardCapture,
                LockdownScenarioArea = scenario.LockdownScenarioArea,
                AllowRecapture = scenario.AllowRecapture,
                EnchantDropsEnabled = scenario.EnchantDropsEnabled,
                Teams = teams.Select(t => new SiegeRuntimeTeamDto
                {
                    Id = t.Id,
                    SortOrder = t.SortOrder,
                    Role = t.Role,
                    AllianceGroup = t.AllianceGroup,
                    ClanId = t.ClanId,
                    // Ready implies a complete identity (TEAM_IDENTITY_INCOMPLETE).
                    Name = SiegeTeamIdentity.ResolveName(t)!,
                    ChatColor = SiegeTeamIdentity.ResolveChatColor(t)!,
                    BannerDesign = SiegeTeamIdentity.ResolveBannerDesign(t) is { } banner
                        ? _mapper.Map<BannerDesignReadDto>(banner)
                        : null,
                    StartMessage = t.StartMessage,
                    Spawnpoints = t.Spawnpoints.OrderBy(p => p.SortOrder).ThenBy(p => p.Id).Select(p => new SiegeRuntimeSpawnpointDto
                    {
                        Id = p.Id,
                        SortOrder = p.SortOrder,
                        Name = p.Name,
                        Location = MapLocation(p.Location),
                        SafeZoneRadius = p.SafeZoneRadius
                    }).ToList()
                }).ToList(),
                Objectives = scenario.Objectives.OrderBy(o => o.SortOrder).ThenBy(o => o.Id).Select(o => new SiegeRuntimeObjectiveDto
                {
                    Id = o.Id,
                    SortOrder = o.SortOrder,
                    Name = o.Name,
                    CaptureLocation = MapLocation(SiegeScenarioReadiness.CaptureLocationOf(o)),
                    GateStructureId = o.GateStructureId,
                    CapturePoints = o.CapturePoints,
                    CaptureRadius = o.CaptureRadius,
                    InstantVictory = o.InstantVictory,
                    InitialHolderTeamId = o.InitialHolderTeamId ?? firstDefenderId,
                    SpawnWhenHeld = o.SpawnWhenHeld,
                    GateStateOnCapture = o.GateStateOnCapture
                }).ToList(),
                Gates = scenario.Gates.OrderBy(g => g.GateStructureId).Select(g => new SiegeRuntimeGateDto
                {
                    GateStructureId = g.GateStructureId,
                    Name = g.GateStructure?.Name,
                    InitialOwnerTeamId = g.InitialOwnerTeamId ?? firstDefenderId,
                    InitialState = g.InitialState,
                    Damageable = g.Damageable,
                    IsObjectiveGate = objectiveGateIds.Contains(g.GateStructureId)
                }).ToList()
            };
        }

        private LocationDto? MapLocation(Location? location) =>
            location == null ? null : _mapper.Map<LocationDto>(location);

        // ==== Apply + validation ====

        private async Task ApplyAsync(SiegeLobby target, SiegeLobbyUpsertDto dto, int? existingId)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            var name = dto.Name?.Trim();
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Name is required.");
            if (name.Length > 100) throw new ArgumentException("Name can be at most 100 characters.");

            var key = dto.Key?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!KeyPattern.IsMatch(key))
                throw new ArgumentException("Key must be 1–64 lowercase letters, digits, '-' or '_' (it is typed in /siege join <key>).");

            if (!Enum.IsDefined(dto.Mode)) throw new ArgumentException($"Unknown mode '{dto.Mode}'.");
            // D1: MVP ships Continuous; Scheduled needs the Phase 10 scheduler, so refuse it rather than
            // save a lobby that would silently never run.
            if (dto.Mode != SiegeLobbyMode.Continuous)
                throw new ArgumentException("Only Continuous lobbies are supported for now (Scheduled mode is a later phase).");

            if (dto.MatchmakingSeconds < 60) throw new ArgumentException("matchmakingSeconds must be at least 60.");
            if (dto.CooldownSeconds < 0) throw new ArgumentException("cooldownSeconds can't be negative.");
            if (dto.VoteCandidateCount is < 1 or > 3) throw new ArgumentException("voteCandidateCount must be 1, 2 or 3.");

            var current = await _repo.GetByKeyAsync(key);
            if (current != null && current.Id != existingId)
                throw new InvalidOperationException($"Lobby key '{key}' is already used by '{current.Name}'.");

            List<SiegeLobbyScenarioUpsertDto>? rotation = null;
            if (dto.Rotation != null)
            {
                rotation = dto.Rotation.Where(r => r != null).ToList();
                if (rotation.Any(r => r.SiegeScenarioId <= 0)) throw new ArgumentException("Every rotation entry needs a siegeScenarioId.");
                if (rotation.Any(r => r.Weight < 1)) throw new ArgumentException("Rotation weights must be 1 or higher.");
                var duplicate = rotation.GroupBy(r => r.SiegeScenarioId).FirstOrDefault(g => g.Count() > 1);
                if (duplicate != null) throw new ArgumentException($"Scenario {duplicate.Key} is in the rotation twice.");

                var existingIds = await _repo.GetExistingScenarioIdsAsync(rotation.Select(r => r.SiegeScenarioId));
                var missing = rotation.Select(r => r.SiegeScenarioId).Except(existingIds).ToList();
                if (missing.Count > 0) throw new ArgumentException($"SiegeScenario with id {missing[0]} not found.");
                // Unready scenarios are allowed: authoring may still be in progress, and runtime-config
                // leaves them out until they are ready.
            }

            target.Name = name;
            target.Key = key;
            target.IsEnabled = dto.IsEnabled;
            target.Mode = dto.Mode;
            target.MatchmakingSeconds = dto.MatchmakingSeconds;
            target.CooldownSeconds = dto.CooldownSeconds;
            target.VoteCandidateCount = dto.VoteCandidateCount;
            target.AllowRandomVote = dto.AllowRandomVote;
            target.ScheduleJson = string.IsNullOrWhiteSpace(dto.ScheduleJson) ? null : dto.ScheduleJson;

            if (rotation != null) SyncRotation(target, rotation);
        }

        // Diff rather than Clear()+re-add (same composite-key tracking reason as the scenario joins).
        private static void SyncRotation(SiegeLobby target, List<SiegeLobbyScenarioUpsertDto> rotation)
        {
            var wanted = rotation.Select(r => r.SiegeScenarioId).ToHashSet();
            foreach (var stale in target.Rotation.Where(r => !wanted.Contains(r.SiegeScenarioId)).ToList())
                target.Rotation.Remove(stale);
            foreach (var entry in rotation)
            {
                var row = target.Rotation.FirstOrDefault(r => r.SiegeScenarioId == entry.SiegeScenarioId);
                if (row == null)
                {
                    row = new SiegeLobbyScenario { SiegeLobbyId = target.Id, SiegeScenarioId = entry.SiegeScenarioId };
                    target.Rotation.Add(row);
                }
                row.Weight = entry.Weight;
            }
        }
    }
}
