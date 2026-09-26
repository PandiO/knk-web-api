using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    // Siege Phase 2 (docs/specs/siege-minigame/DESIGN.md §3.3–3.7, §3.9). The scenario aggregate:
    // the scenario with its district/gate M2M joins, plus its owned teams, spawnpoints and
    // objectives (each with their own endpoints - the scenario's own create/update ignores them).
    //
    // Validation happens in two places. On save, each row's own fields are checked where that
    // doesn't depend on later saves (the scenario is authored in several saves, so "≥ 2 teams" can't
    // be a save rule). GET …/readiness then runs the full §3.9 set: the structural rules
    // (SiegeScenarioReadiness) and the spatial rules below, which reuse the LocationInsideRegion
    // field-validation rule (and so the plugin's region endpoint) rather than re-implementing it.
    //
    // Error convention: ArgumentException -> 400, KeyNotFoundException -> 404,
    // InvalidOperationException -> 409.
    public class SiegeScenarioService : ISiegeScenarioService
    {
        public const string LocationInsideRegionType = "LocationInsideRegion";

        private readonly ISiegeScenarioRepository _repo;
        private readonly ILocationService _locationService;
        private readonly IValidationMethod? _locationInsideRegion;
        private readonly IMapper _mapper;

        public SiegeScenarioService(
            ISiegeScenarioRepository repo,
            ILocationService locationService,
            IEnumerable<IValidationMethod> validationMethods,
            IMapper mapper)
        {
            _repo = repo;
            _locationService = locationService;
            _locationInsideRegion = validationMethods.FirstOrDefault(v => v.ValidationType == LocationInsideRegionType);
            _mapper = mapper;
        }

        // ==== Scenario ====

        public async Task<IEnumerable<SiegeScenarioListDto>> GetAllAsync()
        {
            return _mapper.Map<IEnumerable<SiegeScenarioListDto>>(await _repo.GetAllAsync());
        }

        public async Task<SiegeScenarioReadDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var entity = await _repo.GetByIdAsync(id);
            return entity == null ? null : _mapper.Map<SiegeScenarioReadDto>(entity);
        }

        public async Task<SiegeScenarioReadDto> CreateAsync(SiegeScenarioUpsertDto dto)
        {
            var entity = new SiegeScenario();
            await ApplyScenarioAsync(entity, dto, isCreate: true);
            await _repo.AddAsync(entity);

            // Re-read so the response carries town/district/gate names.
            var created = await _repo.GetByIdAsync(entity.Id);
            return _mapper.Map<SiegeScenarioReadDto>(created ?? entity);
        }

        public async Task UpdateAsync(int id, SiegeScenarioUpsertDto dto)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeScenario with id {id} not found.");
            await ApplyScenarioAsync(existing, dto, isCreate: false);
            await _repo.UpdateAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeScenario with id {id} not found.");
            if (await _repo.HasMatchHistoryAsync(id))
                throw new InvalidOperationException(
                    "This scenario has match history and can't be deleted. Remove it from the lobby rotations instead.");

            // Owned teams/spawnpoints/objectives and the join rows cascade; the town, locations,
            // gates and clans it references are left untouched.
            await _repo.DeleteAsync(existing);
        }

        public async Task<PagedResultDto<SiegeScenarioListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));
            var result = await _repo.SearchAsync(_mapper.Map<PagedQuery>(queryDto));
            return _mapper.Map<PagedResultDto<SiegeScenarioListDto>>(result);
        }

        // ==== Readiness (§3.9) ====

        public async Task<SiegeScenarioReadinessDto> GetReadinessAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var scenario = await _repo.GetByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeScenario with id {id} not found.");

            var result = SiegeScenarioReadiness.EvaluateStructure(scenario);
            await AddSpatialChecksAsync(scenario, result);
            result.IsReady = result.Errors.Count == 0;
            return result;
        }

        // Hub, spawnpoint and objective capture locations must lie inside the town's WorldGuard
        // region. If the check can't run (no validator, town without a region, plugin unreachable)
        // that is a warning, not an error: authoring without the Minecraft server running must still
        // be able to reach "ready", and SpatialChecksRun tells the panel the check didn't happen.
        private async Task AddSpatialChecksAsync(SiegeScenario scenario, SiegeScenarioReadinessDto result)
        {
            if (_locationInsideRegion == null)
            {
                SiegeScenarioReadiness.Warning(result, SiegeReadinessCodes.SpatialChecksUnavailable,
                    "Location-inside-town checks aren't available on this server, so they were skipped.");
                return;
            }

            var town = scenario.Town;
            if (town == null || string.IsNullOrWhiteSpace(town.WgRegionId))
            {
                SiegeScenarioReadiness.Warning(result, SiegeReadinessCodes.TownHasNoRegion,
                    "The scenario's town has no WorldGuard region, so locations couldn't be checked against it.");
                return;
            }

            var points = new List<(Location location, string code, string label, string entityType, int entityId)>();
            if (scenario.HubLocation != null)
                points.Add((scenario.HubLocation, SiegeReadinessCodes.HubOutsideTown, "Hub location", nameof(SiegeScenario), scenario.Id));
            foreach (var team in scenario.Teams.OrderBy(t => t.SortOrder).ThenBy(t => t.Id))
                foreach (var spawn in team.Spawnpoints.OrderBy(p => p.SortOrder).ThenBy(p => p.Id))
                    if (spawn.Location != null)
                        points.Add((spawn.Location, SiegeReadinessCodes.SpawnpointOutsideTown,
                            $"Spawnpoint '{spawn.Name}' of team {SiegeScenarioReadiness.TeamLabel(team)}", nameof(SiegeSpawnpoint), spawn.Id));
            foreach (var objective in scenario.Objectives.OrderBy(o => o.SortOrder).ThenBy(o => o.Id))
                if (SiegeScenarioReadiness.CaptureLocationOf(objective) is { } capture)
                    points.Add((capture, SiegeReadinessCodes.ObjectiveOutsideTown,
                        $"Objective '{objective.Name}' capture point", nameof(SiegeObjective), objective.Id));

            // The validator reads the region through a property path; a dictionary is the shape its
            // path lookup handles for any caller (form JSON arrives the same way).
            var townValue = new Dictionary<string, object>
            {
                ["WgRegionId"] = town.WgRegionId,
                ["Name"] = town.Name ?? "the town"
            };

            foreach (var point in points)
            {
                var check = await _locationInsideRegion.ValidateAsync(point.location, townValue, null, null);
                if (check.IsValid) continue;

                if (check.Metadata != null && check.Metadata.TryGetValue("failureReason", out var reason)
                    && Equals(reason, "PluginUnreachable"))
                {
                    SiegeScenarioReadiness.Warning(result, SiegeReadinessCodes.SpatialChecksUnavailable,
                        "Couldn't check that locations are inside the town: the Minecraft server or knk-plugin isn't reachable. " +
                        "Start it and re-check readiness.");
                    return;
                }

                SiegeScenarioReadiness.Error(result, point.code, $"{point.label}: {check.Message}", point.entityType, point.entityId);
            }

            result.SpatialChecksRun = true;
        }

        // ==== Teams ====

        public async Task<List<SiegeTeamReadDto>> GetTeamsAsync(int siegeScenarioId)
        {
            await RequireScenarioAsync(siegeScenarioId);
            return _mapper.Map<List<SiegeTeamReadDto>>(await _repo.GetTeamsAsync(siegeScenarioId));
        }

        public async Task<SiegeTeamReadDto?> GetTeamByIdAsync(int id)
        {
            if (id <= 0) return null;
            var team = await _repo.GetTeamByIdAsync(id);
            return team == null ? null : _mapper.Map<SiegeTeamReadDto>(team);
        }

        public async Task<SiegeTeamReadDto> CreateTeamAsync(int siegeScenarioId, SiegeTeamUpsertDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (dto.SiegeScenarioId is > 0 && dto.SiegeScenarioId.Value != siegeScenarioId)
                throw new ArgumentException("siegeScenarioId in the body doesn't match the scenario in the URL.");

            var scenario = await RequireScenarioAsync(siegeScenarioId);
            var team = new SiegeTeam { SiegeScenarioId = siegeScenarioId };
            await ApplyTeamAsync(team, dto);
            team.SortOrder = dto.SortOrder ?? NextSortOrder(scenario.Teams.Select(t => t.SortOrder));
            if (team.SortOrder < 0) throw new ArgumentException("sortOrder can't be negative.");

            await _repo.AddTeamAsync(team);
            var created = await _repo.GetTeamByIdAsync(team.Id);
            return _mapper.Map<SiegeTeamReadDto>(created ?? team);
        }

        public async Task UpdateTeamAsync(int id, SiegeTeamUpsertDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var team = await _repo.GetTeamByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeTeam with id {id} not found.");
            if (dto.SiegeScenarioId is > 0 && dto.SiegeScenarioId.Value != team.SiegeScenarioId)
                throw new ArgumentException("A team can't be moved to another scenario; delete it and add it there instead.");

            await ApplyTeamAsync(team, dto);
            if (dto.SortOrder.HasValue)
            {
                if (dto.SortOrder.Value < 0) throw new ArgumentException("sortOrder can't be negative.");
                team.SortOrder = dto.SortOrder.Value;
            }
            await _repo.UpdateTeamAsync(team);
        }

        public async Task DeleteTeamAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var team = await _repo.GetTeamByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeTeam with id {id} not found.");
            // Spawnpoints cascade; objectives/gates naming this team as holder/owner fall back to
            // the first Defender team (SetNull).
            await _repo.DeleteTeamAsync(team);
        }

        public async Task<PagedResultDto<SiegeTeamReadDto>> SearchTeamsAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));
            var result = await _repo.SearchTeamsAsync(_mapper.Map<PagedQuery>(queryDto));
            return _mapper.Map<PagedResultDto<SiegeTeamReadDto>>(result);
        }

        // ==== Spawnpoints ====

        public async Task<List<SiegeSpawnpointReadDto>> GetSpawnpointsAsync(int siegeTeamId)
        {
            _ = await _repo.GetTeamByIdAsync(siegeTeamId)
                ?? throw new KeyNotFoundException($"SiegeTeam with id {siegeTeamId} not found.");
            return _mapper.Map<List<SiegeSpawnpointReadDto>>(await _repo.GetSpawnpointsAsync(siegeTeamId));
        }

        public async Task<SiegeSpawnpointReadDto?> GetSpawnpointByIdAsync(int id)
        {
            if (id <= 0) return null;
            var spawnpoint = await _repo.GetSpawnpointByIdAsync(id);
            return spawnpoint == null ? null : _mapper.Map<SiegeSpawnpointReadDto>(spawnpoint);
        }

        public async Task<SiegeSpawnpointReadDto> CreateSpawnpointAsync(int siegeTeamId, SiegeSpawnpointUpsertDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (dto.SiegeTeamId is > 0 && dto.SiegeTeamId.Value != siegeTeamId)
                throw new ArgumentException("siegeTeamId in the body doesn't match the team in the URL.");

            var team = await _repo.GetTeamByIdAsync(siegeTeamId)
                ?? throw new KeyNotFoundException($"SiegeTeam with id {siegeTeamId} not found.");

            var spawnpoint = new SiegeSpawnpoint { SiegeTeamId = siegeTeamId };
            await ApplySpawnpointAsync(spawnpoint, dto);
            spawnpoint.SortOrder = dto.SortOrder ?? NextSortOrder(team.Spawnpoints.Select(p => p.SortOrder));
            if (spawnpoint.SortOrder < 0) throw new ArgumentException("sortOrder can't be negative.");

            await _repo.AddSpawnpointAsync(spawnpoint);
            var created = await _repo.GetSpawnpointByIdAsync(spawnpoint.Id);
            return _mapper.Map<SiegeSpawnpointReadDto>(created ?? spawnpoint);
        }

        public async Task UpdateSpawnpointAsync(int id, SiegeSpawnpointUpsertDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var spawnpoint = await _repo.GetSpawnpointByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeSpawnpoint with id {id} not found.");
            if (dto.SiegeTeamId is > 0 && dto.SiegeTeamId.Value != spawnpoint.SiegeTeamId)
                throw new ArgumentException("A spawnpoint can't be moved to another team; delete it and add it there instead.");

            await ApplySpawnpointAsync(spawnpoint, dto);
            if (dto.SortOrder.HasValue)
            {
                if (dto.SortOrder.Value < 0) throw new ArgumentException("sortOrder can't be negative.");
                spawnpoint.SortOrder = dto.SortOrder.Value;
            }
            await _repo.UpdateSpawnpointAsync(spawnpoint);
        }

        public async Task DeleteSpawnpointAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var spawnpoint = await _repo.GetSpawnpointByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeSpawnpoint with id {id} not found.");
            // The Location row stays (Restrict) - it may be reused.
            await _repo.DeleteSpawnpointAsync(spawnpoint);
        }

        // ==== Objectives ====

        public async Task<List<SiegeObjectiveReadDto>> GetObjectivesAsync(int siegeScenarioId)
        {
            await RequireScenarioAsync(siegeScenarioId);
            return _mapper.Map<List<SiegeObjectiveReadDto>>(await _repo.GetObjectivesAsync(siegeScenarioId));
        }

        public async Task<SiegeObjectiveReadDto?> GetObjectiveByIdAsync(int id)
        {
            if (id <= 0) return null;
            var objective = await _repo.GetObjectiveByIdAsync(id);
            return objective == null ? null : _mapper.Map<SiegeObjectiveReadDto>(objective);
        }

        public async Task<SiegeObjectiveReadDto> CreateObjectiveAsync(int siegeScenarioId, SiegeObjectiveUpsertDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (dto.SiegeScenarioId is > 0 && dto.SiegeScenarioId.Value != siegeScenarioId)
                throw new ArgumentException("siegeScenarioId in the body doesn't match the scenario in the URL.");

            var scenario = await RequireScenarioAsync(siegeScenarioId);
            var objective = new SiegeObjective { SiegeScenarioId = siegeScenarioId };
            await ApplyObjectiveAsync(objective, dto, scenario);
            objective.SortOrder = dto.SortOrder ?? NextSortOrder(scenario.Objectives.Select(o => o.SortOrder));
            if (objective.SortOrder < 0) throw new ArgumentException("sortOrder can't be negative.");

            await _repo.AddObjectiveAsync(objective);
            var created = await _repo.GetObjectiveByIdAsync(objective.Id);
            return _mapper.Map<SiegeObjectiveReadDto>(created ?? objective);
        }

        public async Task UpdateObjectiveAsync(int id, SiegeObjectiveUpsertDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var objective = await _repo.GetObjectiveByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeObjective with id {id} not found.");
            if (dto.SiegeScenarioId is > 0 && dto.SiegeScenarioId.Value != objective.SiegeScenarioId)
                throw new ArgumentException("An objective can't be moved to another scenario; delete it and add it there instead.");

            var scenario = await RequireScenarioAsync(objective.SiegeScenarioId);
            await ApplyObjectiveAsync(objective, dto, scenario);
            if (dto.SortOrder.HasValue)
            {
                if (dto.SortOrder.Value < 0) throw new ArgumentException("sortOrder can't be negative.");
                objective.SortOrder = dto.SortOrder.Value;
            }
            await _repo.UpdateObjectiveAsync(objective);
        }

        public async Task DeleteObjectiveAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var objective = await _repo.GetObjectiveByIdAsync(id)
                ?? throw new KeyNotFoundException($"SiegeObjective with id {id} not found.");
            await _repo.DeleteObjectiveAsync(objective);
        }

        // ==== Apply + save-time validation ====

        private async Task ApplyScenarioAsync(SiegeScenario target, SiegeScenarioUpsertDto dto, bool isCreate)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            var name = RequireText(dto.Name, "Name", 100);
            var description = OptionalText(dto.Description, "Description", 1000);

            if (dto.TownId <= 0) throw new ArgumentException("A town is required.");
            var town = await _repo.GetTownAsync(dto.TownId)
                ?? throw new ArgumentException($"Town with id {dto.TownId} not found.");

            var hubLocationId = await ResolveLocationAsync(dto.HubLocationId, dto.HubLocation, "hub location")
                ?? throw new ArgumentException("A hub location is required (capture it in-game).");

            if (dto.PlayersMin < 1) throw new ArgumentException("playersMin must be at least 1.");
            if (dto.PlayersMax < dto.PlayersMin) throw new ArgumentException("playersMax must be at least playersMin.");
            if (dto.MatchDurationMinSeconds <= 0 || dto.MatchDurationPerPlayerSeconds <= 0 || dto.MatchDurationMaxSeconds <= 0)
                throw new ArgumentException("Match durations must be positive.");
            if (dto.MatchDurationMinSeconds > dto.MatchDurationMaxSeconds)
                throw new ArgumentException("matchDurationMinSeconds can't be above matchDurationMaxSeconds.");
            if (new[] { dto.CoinRewardWin, dto.ExpRewardWin, dto.GemRewardWin, dto.CoinRewardHolding,
                        dto.ExpRewardHolding, dto.CoinRewardCapture, dto.ExpRewardCapture }.Any(r => r < 0))
                throw new ArgumentException("Rewards can't be negative.");

            // The wizard can send 0 for an empty optional picker - treat it as "none".
            int? minTitleBracketId = dto.MinTitleBracketId is > 0 ? dto.MinTitleBracketId : null;
            if (minTitleBracketId.HasValue && !await _repo.TitleBracketExistsAsync(minTitleBracketId.Value))
                throw new ArgumentException($"TitleBracket with id {minTitleBracketId} not found.");

            // Districts (replace-set; null = unchanged). Every district must belong to the town.
            var districtIds = dto.Districts == null
                ? target.Districts.Select(d => d.DistrictId).ToList()
                : dto.Districts.Select(d => d.DistrictId).Where(id => id > 0).Distinct().ToList();
            if (districtIds.Count > 0)
            {
                var districts = await _repo.GetDistrictsAsync(districtIds);
                var missing = districtIds.Except(districts.Select(d => d.Id)).ToList();
                if (missing.Count > 0) throw new ArgumentException($"District with id {missing[0]} not found.");
                var foreign = districts.FirstOrDefault(d => d.TownId != town.Id);
                if (foreign != null) throw new ArgumentException($"District '{foreign.Name}' doesn't belong to town '{town.Name}'.");
            }

            // Gates (replace-set; null = unchanged). Must exist, be in the scenario area, start in a
            // resting state, and name an owner team of this scenario (a new scenario has none yet).
            List<SiegeScenarioGateUpsertDto>? gateDtos = null;
            if (dto.Gates != null)
            {
                gateDtos = dto.Gates.Where(g => g != null).ToList();
                if (gateDtos.Any(g => g.GateStructureId <= 0)) throw new ArgumentException("Every selected gate needs a gateStructureId.");
                var duplicate = gateDtos.GroupBy(g => g.GateStructureId).FirstOrDefault(g => g.Count() > 1);
                if (duplicate != null) throw new ArgumentException($"Gate {duplicate.Key} is selected twice.");

                var gates = await _repo.GetGateStructuresAsync(gateDtos.Select(g => g.GateStructureId));
                var teamIds = isCreate ? new HashSet<int>() : target.Teams.Select(t => t.Id).ToHashSet();
                var districtSet = districtIds.ToHashSet();
                foreach (var gateDto in gateDtos)
                {
                    var gate = gates.FirstOrDefault(g => g.Id == gateDto.GateStructureId)
                        ?? throw new ArgumentException($"GateStructure with id {gateDto.GateStructureId} not found.");
                    if (!SiegeScenarioReadiness.IsInScenarioArea(gate, town.Id, districtSet))
                        throw new ArgumentException(districtSet.Count > 0
                            ? $"Gate '{gate.Name}' isn't in one of the scenario's districts."
                            : $"Gate '{gate.Name}' isn't in town '{town.Name}'.");
                    RequireRestingState(gateDto.InitialState, $"Gate '{gate.Name}' initialState");
                    if (gateDto.InitialOwnerTeamId is > 0 && !teamIds.Contains(gateDto.InitialOwnerTeamId.Value))
                        throw new ArgumentException($"Gate '{gate.Name}': initialOwnerTeamId must be a team of this scenario.");
                }
            }

            target.Name = name;
            target.Description = description;
            target.TownId = town.Id;
            target.HubLocationId = hubLocationId;
            target.PlayersMin = dto.PlayersMin;
            target.PlayersMax = dto.PlayersMax;
            target.MinTitleBracketId = minTitleBracketId;
            target.MatchDurationMinSeconds = dto.MatchDurationMinSeconds;
            target.MatchDurationPerPlayerSeconds = dto.MatchDurationPerPlayerSeconds;
            target.MatchDurationMaxSeconds = dto.MatchDurationMaxSeconds;
            target.CoinRewardWin = dto.CoinRewardWin;
            target.ExpRewardWin = dto.ExpRewardWin;
            target.GemRewardWin = dto.GemRewardWin;
            target.CoinRewardHolding = dto.CoinRewardHolding;
            target.ExpRewardHolding = dto.ExpRewardHolding;
            target.CoinRewardCapture = dto.CoinRewardCapture;
            target.ExpRewardCapture = dto.ExpRewardCapture;
            target.LockdownScenarioArea = dto.LockdownScenarioArea;
            target.AllowRecapture = dto.AllowRecapture;
            target.EnchantDropsEnabled = dto.EnchantDropsEnabled;

            if (dto.Districts != null) SyncDistricts(target, districtIds);
            if (gateDtos != null) SyncGates(target, gateDtos);
        }

        // Diff instead of Clear()+re-add: re-adding a join row with the key of one just removed makes
        // EF track two instances of the same composite key.
        private static void SyncDistricts(SiegeScenario target, List<int> districtIds)
        {
            foreach (var stale in target.Districts.Where(d => !districtIds.Contains(d.DistrictId)).ToList())
                target.Districts.Remove(stale);
            foreach (var id in districtIds.Where(id => target.Districts.All(d => d.DistrictId != id)))
                target.Districts.Add(new SiegeScenarioDistrict { SiegeScenarioId = target.Id, DistrictId = id });
        }

        private static void SyncGates(SiegeScenario target, List<SiegeScenarioGateUpsertDto> gateDtos)
        {
            var wanted = gateDtos.Select(g => g.GateStructureId).ToHashSet();
            foreach (var stale in target.Gates.Where(g => !wanted.Contains(g.GateStructureId)).ToList())
                target.Gates.Remove(stale);
            foreach (var gateDto in gateDtos)
            {
                var row = target.Gates.FirstOrDefault(g => g.GateStructureId == gateDto.GateStructureId);
                if (row == null)
                {
                    row = new SiegeScenarioGate { SiegeScenarioId = target.Id, GateStructureId = gateDto.GateStructureId };
                    target.Gates.Add(row);
                }
                row.InitialOwnerTeamId = gateDto.InitialOwnerTeamId is > 0 ? gateDto.InitialOwnerTeamId : null;
                row.InitialState = gateDto.InitialState;
                row.Damageable = gateDto.Damageable;
            }
        }

        private async Task ApplyTeamAsync(SiegeTeam target, SiegeTeamUpsertDto dto)
        {
            if (!Enum.IsDefined(dto.Role)) throw new ArgumentException($"Unknown role '{dto.Role}'.");
            if (dto.AllianceGroup < 1) throw new ArgumentException("allianceGroup must be 1 or higher.");

            int? clanId = dto.ClanId is > 0 ? dto.ClanId : null;
            if (clanId.HasValue && await _repo.GetClanAsync(clanId.Value) == null)
                throw new ArgumentException($"Clan with id {clanId} not found.");

            int? bannerDesignId = dto.BannerDesignId is > 0 ? dto.BannerDesignId : null;
            if (bannerDesignId.HasValue && !await _repo.BannerDesignExistsAsync(bannerDesignId.Value))
                throw new ArgumentException($"BannerDesign with id {bannerDesignId} not found.");

            string? chatColor = null;
            if (!string.IsNullOrWhiteSpace(dto.ChatColor))
            {
                chatColor = dto.ChatColor.Trim().ToUpperInvariant();
                if (!ClanService.ChatColorNames.Contains(chatColor))
                    throw new ArgumentException($"Unknown chat colour '{dto.ChatColor}'. Use a Bukkit colour name such as GOLD or DARK_RED.");
            }

            var name = OptionalText(dto.Name, "Name", 100);

            // Ad-hoc team (no clan): the identity lives only on this row, so all three are needed
            // (DESIGN §3.4). Checked on save because the team form has every identity field.
            if (!clanId.HasValue && (name == null || chatColor == null || !bannerDesignId.HasValue))
                throw new ArgumentException("A team without a clan needs a name, a chat colour and a banner.");

            target.Role = dto.Role;
            target.AllianceGroup = dto.AllianceGroup;
            target.ClanId = clanId;
            target.Name = name;
            target.ChatColor = chatColor;
            target.BannerDesignId = bannerDesignId;
            target.StartMessage = OptionalText(dto.StartMessage, "startMessage", 255);
        }

        private async Task ApplySpawnpointAsync(SiegeSpawnpoint target, SiegeSpawnpointUpsertDto dto)
        {
            var name = RequireText(dto.Name, "Name", 100);
            var locationId = await ResolveLocationAsync(dto.LocationId, dto.Location, "spawnpoint location")
                ?? throw new ArgumentException("A spawnpoint needs a location (capture it in-game).");
            if (dto.SafeZoneRadius < 0 || dto.SafeZoneRadius > 64)
                throw new ArgumentException("safeZoneRadius must be between 0 and 64.");

            target.Name = name;
            target.LocationId = locationId;
            target.SafeZoneRadius = dto.SafeZoneRadius;
        }

        private async Task ApplyObjectiveAsync(SiegeObjective target, SiegeObjectiveUpsertDto dto, SiegeScenario scenario)
        {
            var name = RequireText(dto.Name, "Name", 100);
            if (dto.CapturePoints <= 0) throw new ArgumentException("capturePoints must be positive.");
            if (dto.CaptureRadius <= 0 || dto.CaptureRadius > 32) throw new ArgumentException("captureRadius must be above 0 and at most 32.");
            RequireRestingState(dto.GateStateOnCapture, "gateStateOnCapture");

            var locationId = await ResolveLocationAsync(dto.LocationId, dto.Location, "objective location");

            // D3: an objective's gate must first be selected in the scenario's Gates step (step 6 comes
            // before Objectives, step 7). Removing it from Gates later is caught by readiness instead.
            int? gateStructureId = dto.GateStructureId is > 0 ? dto.GateStructureId : null;
            GateStructure? gate = null;
            if (gateStructureId.HasValue)
            {
                if (scenario.Gates.All(g => g.GateStructureId != gateStructureId.Value))
                    throw new ArgumentException("The objective's gate must be one of the scenario's selected gates - add it under Gates first.");
                gate = scenario.Gates.First(g => g.GateStructureId == gateStructureId.Value).GateStructure
                    ?? (await _repo.GetGateStructuresAsync(new[] { gateStructureId.Value })).FirstOrDefault();
            }

            // Location rule (§3.6): own location, else the gate's.
            if (!locationId.HasValue && gate?.LocationId == null)
                throw new ArgumentException(gateStructureId.HasValue
                    ? "The objective's gate has no location, so the objective needs its own capture location."
                    : "An objective needs a capture location or a gate.");

            int? holderTeamId = dto.InitialHolderTeamId is > 0 ? dto.InitialHolderTeamId : null;
            if (holderTeamId.HasValue && scenario.Teams.All(t => t.Id != holderTeamId.Value))
                throw new ArgumentException("initialHolderTeamId must be a team of this scenario.");

            target.Name = name;
            target.LocationId = locationId;
            target.GateStructureId = gateStructureId;
            target.CapturePoints = dto.CapturePoints;
            target.CaptureRadius = dto.CaptureRadius;
            target.InstantVictory = dto.InstantVictory;
            target.InitialHolderTeamId = holderTeamId;
            target.SpawnWhenHeld = dto.SpawnWhenHeld;
            target.GateStateOnCapture = dto.GateStateOnCapture;
        }

        // ==== Helpers ====

        private async Task<SiegeScenario> RequireScenarioAsync(int siegeScenarioId)
        {
            if (siegeScenarioId <= 0) throw new KeyNotFoundException($"SiegeScenario with id {siegeScenarioId} not found.");
            return await _repo.GetByIdAsync(siegeScenarioId)
                ?? throw new KeyNotFoundException($"SiegeScenario with id {siegeScenarioId} not found.");
        }

        // GateStructureService's world-bound location convention: an id of an existing Location, or
        // an inline location (created when it has no id, updated when it has one).
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

        private static int NextSortOrder(IEnumerable<int> existing)
        {
            var list = existing.ToList();
            return list.Count == 0 ? 0 : list.Max() + 1;
        }

        private static void RequireRestingState(GateDoorOpenState state, string field)
        {
            if (state != GateDoorOpenState.OPEN && state != GateDoorOpenState.CLOSED)
                throw new ArgumentException($"{field} must be OPEN or CLOSED.");
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
