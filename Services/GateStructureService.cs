using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    public class GateStructureService : IGateStructureService
    {
        private readonly IGateStructureRepository _repo;
        private readonly ILocationRepository _locationRepo;
        private readonly ILocationService _locationService;
        private readonly IMapper _mapper;

        public GateStructureService(
            IGateStructureRepository repo,
            ILocationRepository locationRepo,
            ILocationService locationService,
            IMapper mapper)
        {
            _repo = repo;
            _locationRepo = locationRepo;
            _locationService = locationService;
            _mapper = mapper;
        }

        public async Task<IEnumerable<GateStructureDto>> GetAllAsync()
        {
            var gateStructures = await _repo.GetAllAsync();
            return _mapper.Map<IEnumerable<GateStructureDto>>(gateStructures);
        }

        public async Task<GateStructureDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var gateStructure = await _repo.GetByIdAsync(id);
            return _mapper.Map<GateStructureDto>(gateStructure);
        }

        public async Task<GateStructureDto?> GetByIdWithSnapshotsAsync(int id)
        {
            if (id <= 0) return null;
            var gateStructure = await _repo.GetByIdWithSnapshotsAsync(id);
            return _mapper.Map<GateStructureDto>(gateStructure);
        }

        public async Task<IEnumerable<GateStructureDto>> GetGatesByDomainAsync(int domainId)
        {
            if (domainId <= 0)
                throw new ArgumentException("Invalid domainId.", nameof(domainId));

            var gateStructures = await _repo.GetGatesByDomainAsync(domainId);
            return _mapper.Map<IEnumerable<GateStructureDto>>(gateStructures);
        }

        public async Task<GateStructureDto> CreateAsync(GateStructureDto gateStructureDto)
        {
            if (gateStructureDto == null)
                throw new ArgumentNullException(nameof(gateStructureDto));
            if (string.IsNullOrWhiteSpace(gateStructureDto.Name))
                throw new ArgumentException("GateStructure name is required.", nameof(gateStructureDto));
            if (gateStructureDto.StreetId <= 0)
                throw new ArgumentException("Valid StreetId is required.", nameof(gateStructureDto));
            if (gateStructureDto.DistrictId <= 0)
                throw new ArgumentException("Valid DistrictId is required.", nameof(gateStructureDto));

            var gateStructure = _mapper.Map<GateStructure>(gateStructureDto);
            await ApplyLocationReferencesAsync(gateStructure, gateStructureDto, isCreate: true);
            await _repo.AddGateStructureAsync(gateStructure);
            return _mapper.Map<GateStructureDto>(gateStructure);
        }

        public async Task UpdateAsync(int id, GateStructureDto gateStructureDto)
        {
            if (gateStructureDto == null)
                throw new ArgumentNullException(nameof(gateStructureDto));
            if (id <= 0)
                throw new ArgumentException("Invalid id.", nameof(id));
            if (string.IsNullOrWhiteSpace(gateStructureDto.Name))
                throw new ArgumentException("GateStructure name is required.", nameof(gateStructureDto));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null)
                throw new KeyNotFoundException($"GateStructure with id {id} not found.");

            _mapper.Map(gateStructureDto, existing);
            await ApplyLocationReferencesAsync(existing, gateStructureDto);
            await _repo.UpdateGateStructureAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0)
                throw new ArgumentException("Invalid id.", nameof(id));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null)
                throw new KeyNotFoundException($"GateStructure with id {id} not found.");

            // Siege scenarios/objectives/match snapshots reference gates with Restrict
            // (docs/specs/siege-minigame/DESIGN.md §3.7) - refuse with a readable 409 instead of the
            // FK error.
            if (await _repo.IsReferencedBySiegeAsync(id))
                throw new InvalidOperationException(
                    "This gate is used by a siege scenario (selected gate or objective). Remove it there before deleting it.");

            // GateDoors (and their block snapshots) cascade-delete at the DB level.
            await _repo.DeleteGateStructureAsync(id);
        }

        public async Task<PagedResultDto<GateStructureListDto>> SearchAsync(PagedQueryDto query)
        {
            if (query == null)
                throw new ArgumentNullException(nameof(query));

            var result = await _repo.SearchAsync(_mapper.Map<PagedQuery>(query));
            return new PagedResultDto<GateStructureListDto>
            {
                Items = _mapper.Map<List<GateStructureListDto>>(result.Items),
                TotalCount = result.TotalCount,
                PageNumber = result.PageNumber,
                PageSize = result.PageSize
            };
        }

        public async Task UpdateOverridesAsync(int id, GateStructureOverridesUpdateDto overridesDto)
        {
            if (overridesDto == null)
                throw new ArgumentNullException(nameof(overridesDto));
            if (id <= 0)
                throw new ArgumentException("Invalid id.", nameof(id));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null)
                throw new KeyNotFoundException($"GateStructure with id {id} not found.");

            // Each override field: an explicit "clear" flag nulls it out (reverting every door
            // to its own value); otherwise a provided value sets it; a field mentioned as neither
            // is left untouched. See decision 5.0-B.
            ApplyOverride(overridesDto.ClearIsActiveOverride, overridesDto.IsActiveOverride, v => existing.IsActiveOverride = v);
            ApplyOverride(overridesDto.ClearCanRespawnOverride, overridesDto.CanRespawnOverride, v => existing.CanRespawnOverride = v);
            ApplyOverride(overridesDto.ClearIsDestroyedOverride, overridesDto.IsDestroyedOverride, v => existing.IsDestroyedOverride = v);
            ApplyOverride(overridesDto.ClearIsInvincibleOverride, overridesDto.IsInvincibleOverride, v => existing.IsInvincibleOverride = v);
            ApplyOverride(overridesDto.ClearOpenedStateOverride, overridesDto.OpenedStateOverride, v => existing.OpenedStateOverride = v);
            ApplyOverride(overridesDto.ClearAllowPassThroughOverride, overridesDto.AllowPassThroughOverride, v => existing.AllowPassThroughOverride = v);
            ApplyOverride(overridesDto.ClearPassThroughDurationSecondsOverride, overridesDto.PassThroughDurationSecondsOverride, v => existing.PassThroughDurationSecondsOverride = v);
            ApplyOverride(overridesDto.ClearShowHealthDisplayOverride, overridesDto.ShowHealthDisplayOverride, v => existing.ShowHealthDisplayOverride = v);
            ApplyOverride(overridesDto.ClearHealthDisplayModeOverride, overridesDto.HealthDisplayModeOverride, v => existing.HealthDisplayModeOverride = v);
            ApplyOverride(overridesDto.ClearHealthDisplayYOffsetOverride, overridesDto.HealthDisplayYOffsetOverride, v => existing.HealthDisplayYOffsetOverride = v);
            ApplyOverride(overridesDto.ClearGateNameDisplayModeOverride, overridesDto.GateNameDisplayModeOverride, v => existing.GateNameDisplayModeOverride = v);
            ApplyOverride(overridesDto.ClearStatusDisplayModeOverride, overridesDto.StatusDisplayModeOverride, v => existing.StatusDisplayModeOverride = v);
            ApplyOverride(overridesDto.ClearAllowContinuousDamageOverride, overridesDto.AllowContinuousDamageOverride, v => existing.AllowContinuousDamageOverride = v);
            ApplyOverride(overridesDto.ClearContinuousDamageMultiplierOverride, overridesDto.ContinuousDamageMultiplierOverride, v => existing.ContinuousDamageMultiplierOverride = v);

            await _repo.UpdateGateStructureAsync(existing);
        }

        private static void ApplyOverride<T>(bool clear, T? value, Action<T?> setter) where T : struct
        {
            if (clear)
            {
                setter(null);
            }
            else if (value.HasValue)
            {
                setter(value);
            }
        }

        private async Task ApplyLocationReferencesAsync(GateStructure gateStructure, GateStructureDto gateStructureDto, bool isCreate = false)
        {
            gateStructure.LocationId = await ResolveLocationReferenceAsync(
                gateStructureDto.LocationId,
                gateStructureDto.Location,
                "Location");

            var hasGuardInput = gateStructureDto.GuardSpawnLocationIds != null || gateStructureDto.GuardSpawnLocations != null;
            if (!isCreate && !hasGuardInput)
            {
                return;
            }

            var resolvedGuardLocationIds = new List<int>();

            if (gateStructureDto.GuardSpawnLocationIds != null)
            {
                resolvedGuardLocationIds.AddRange(gateStructureDto.GuardSpawnLocationIds.Where(id => id > 0));
            }

            if (gateStructureDto.GuardSpawnLocations != null)
            {
                foreach (var locationDto in gateStructureDto.GuardSpawnLocations)
                {
                    var guardLocationId = await ResolveLocationReferenceAsync(
                        locationDto?.Id,
                        locationDto,
                        "GuardSpawnLocation");

                    if (guardLocationId.HasValue)
                    {
                        resolvedGuardLocationIds.Add(guardLocationId.Value);
                    }
                }
            }

            var distinctGuardIds = resolvedGuardLocationIds.Distinct().ToList();
            var guardLocations = new List<Location>();
            foreach (var guardLocationId in distinctGuardIds)
            {
                var guardLocation = await _locationRepo.GetByIdAsync(guardLocationId);
                if (guardLocation == null)
                {
                    throw new ArgumentException($"Location with id {guardLocationId} not found for GuardSpawnLocations.");
                }

                guardLocations.Add(guardLocation);
            }

            gateStructure.GuardSpawnLocations = guardLocations;
        }

        private async Task<int?> ResolveLocationReferenceAsync(int? locationId, LocationDto? locationDto, string fieldName)
        {
            if (locationDto == null && !locationId.HasValue)
            {
                return null;
            }

            if (locationDto == null && locationId.HasValue)
            {
                var existingLocation = await _locationRepo.GetByIdAsync(locationId.Value);
                if (existingLocation == null)
                {
                    throw new ArgumentException($"Location with id {locationId} not found for {fieldName}.");
                }

                return locationId.Value;
            }

            if (locationDto != null && locationId.HasValue && locationDto.Id.HasValue && locationDto.Id.Value != locationId.Value)
            {
                throw new ArgumentException($"Conflicting location references for {fieldName}: locationId={locationId}, location.id={locationDto.Id}.");
            }

            if (locationDto != null)
            {
                if (!locationDto.Id.HasValue || locationDto.Id.Value == 0)
                {
                    var createdLocation = await _locationService.CreateAsync(locationDto);
                    return createdLocation.Id;
                }

                await _locationService.UpdateAsync(locationDto.Id.Value, locationDto);
                return locationDto.Id.Value;
            }

            return null;
        }
    }
}
