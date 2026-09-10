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

            // Validate health values
            if (gateStructureDto.HealthCurrent.HasValue && gateStructureDto.HealthMax.HasValue)
            {
                if (gateStructureDto.HealthCurrent.Value > gateStructureDto.HealthMax.Value)
                    throw new ArgumentException("HealthCurrent cannot exceed HealthMax.", nameof(gateStructureDto));
            }

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

            // Validate health values
            if (gateStructureDto.HealthCurrent.HasValue && gateStructureDto.HealthMax.HasValue)
            {
                if (gateStructureDto.HealthCurrent.Value > gateStructureDto.HealthMax.Value)
                    throw new ArgumentException("HealthCurrent cannot exceed HealthMax.", nameof(gateStructureDto));
            }

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

            // Delete associated block snapshots first
            await _repo.DeleteBlockSnapshotsByGateIdAsync(id);
            await _repo.DeleteOpenedBlockSnapshotsByGateIdAsync(id);

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

        public async Task<IEnumerable<GateStructureDto>> GetActiveGatesAsync()
        {
            var gates = await _repo.GetActiveGatesAsync();
            return _mapper.Map<IEnumerable<GateStructureDto>>(gates);
        }

        public async Task UpdateHealthAsync(int id, double newHealth)
        {
            if (id <= 0)
                throw new ArgumentException("Invalid id.", nameof(id));
            if (newHealth < 0)
                throw new ArgumentException("Health cannot be negative.", nameof(newHealth));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null)
                throw new KeyNotFoundException($"GateStructure with id {id} not found.");

            if (newHealth > existing.HealthMax)
                throw new ArgumentException($"Health cannot exceed HealthMax ({existing.HealthMax}).", nameof(newHealth));

            await _repo.UpdateGateHealthAsync(id, newHealth);
        }

        public async Task UpdateStateAsync(int id, bool isOpened, bool isDestroyed, bool isJammed)
        {
            if (id <= 0)
                throw new ArgumentException("Invalid id.", nameof(id));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null)
                throw new KeyNotFoundException($"GateStructure with id {id} not found.");

            await _repo.UpdateGateStateAsync(id, isOpened, isDestroyed, isJammed);
        }

        public async Task UpdateOperationalSettingsAsync(int id, bool isActive, bool isInvincible)
        {
            if (id <= 0)
                throw new ArgumentException("Invalid id.", nameof(id));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null)
                throw new KeyNotFoundException($"GateStructure with id {id} not found.");

            await _repo.UpdateGateOperationalSettingsAsync(id, isActive, isInvincible);
        }

        public async Task<IEnumerable<GateBlockSnapshotDto>> GetBlockSnapshotsAsync(int gateId)
        {
            if (gateId <= 0)
                throw new ArgumentException("Invalid gateId.", nameof(gateId));

            var snapshots = await _repo.GetBlockSnapshotsByGateIdAsync(gateId);
            return _mapper.Map<IEnumerable<GateBlockSnapshotDto>>(snapshots);
        }

        public async Task AddBlockSnapshotsAsync(int gateId, IEnumerable<GateBlockSnapshotDto> snapshots)
        {
            if (gateId <= 0)
                throw new ArgumentException("Invalid gateId.", nameof(gateId));
            if (snapshots == null || !snapshots.Any())
                throw new ArgumentException("Snapshots collection cannot be null or empty.", nameof(snapshots));

            var existing = await _repo.GetByIdAsync(gateId);
            if (existing == null)
                throw new KeyNotFoundException($"GateStructure with id {gateId} not found.");

            var snapshotEntities = _mapper.Map<IEnumerable<GateBlockSnapshot>>(snapshots);
            
            // Ensure all snapshots have the correct GateStructureId
            foreach (var snapshot in snapshotEntities)
            {
                snapshot.GateStructureId = gateId;
            }

            await _repo.AddBlockSnapshotsAsync(snapshotEntities);
        }

        public async Task AddBlockSnapshotsAsync(int gateId, IEnumerable<GateBlockSnapshotCreateDto> snapshots)
        {
            if (gateId <= 0)
                throw new ArgumentException("Invalid gateId.", nameof(gateId));
            if (snapshots == null || !snapshots.Any())
                throw new ArgumentException("Snapshots collection cannot be null or empty.", nameof(snapshots));

            var existing = await _repo.GetByIdAsync(gateId);
            if (existing == null)
                throw new KeyNotFoundException($"GateStructure with id {gateId} not found.");

            var snapshotEntities = _mapper.Map<IEnumerable<GateBlockSnapshot>>(snapshots);

            foreach (var snapshot in snapshotEntities)
            {
                snapshot.GateStructureId = gateId;
            }

            await _repo.AddBlockSnapshotsAsync(snapshotEntities);
        }

        public async Task ClearBlockSnapshotsAsync(int gateId)
        {
            if (gateId <= 0)
                throw new ArgumentException("Invalid gateId.", nameof(gateId));

            await _repo.DeleteBlockSnapshotsByGateIdAsync(gateId);
        }

        public async Task<IEnumerable<GateOpenedBlockSnapshotDto>> GetOpenedBlockSnapshotsAsync(int gateId)
        {
            if (gateId <= 0)
                throw new ArgumentException("Invalid gateId.", nameof(gateId));

            var snapshots = await _repo.GetOpenedBlockSnapshotsByGateIdAsync(gateId);
            return _mapper.Map<IEnumerable<GateOpenedBlockSnapshotDto>>(snapshots);
        }

        public async Task AddOpenedBlockSnapshotsAsync(int gateId, IEnumerable<GateOpenedBlockSnapshotDto> snapshots)
        {
            if (gateId <= 0)
                throw new ArgumentException("Invalid gateId.", nameof(gateId));
            if (snapshots == null || !snapshots.Any())
                throw new ArgumentException("Snapshots collection cannot be null or empty.", nameof(snapshots));

            var existing = await _repo.GetByIdAsync(gateId);
            if (existing == null)
                throw new KeyNotFoundException($"GateStructure with id {gateId} not found.");

            var snapshotEntities = _mapper.Map<IEnumerable<GateOpenedBlockSnapshot>>(snapshots);

            foreach (var snapshot in snapshotEntities)
            {
                snapshot.GateStructureId = gateId;
            }

            await _repo.AddOpenedBlockSnapshotsAsync(snapshotEntities);
        }

        public async Task AddOpenedBlockSnapshotsAsync(int gateId, IEnumerable<GateOpenedBlockSnapshotCreateDto> snapshots)
        {
            if (gateId <= 0)
                throw new ArgumentException("Invalid gateId.", nameof(gateId));
            if (snapshots == null || !snapshots.Any())
                throw new ArgumentException("Snapshots collection cannot be null or empty.", nameof(snapshots));

            var existing = await _repo.GetByIdAsync(gateId);
            if (existing == null)
                throw new KeyNotFoundException($"GateStructure with id {gateId} not found.");

            var snapshotEntities = _mapper.Map<IEnumerable<GateOpenedBlockSnapshot>>(snapshots);

            foreach (var snapshot in snapshotEntities)
            {
                snapshot.GateStructureId = gateId;
            }

            await _repo.AddOpenedBlockSnapshotsAsync(snapshotEntities);
        }

        public async Task ClearOpenedBlockSnapshotsAsync(int gateId)
        {
            if (gateId <= 0)
                throw new ArgumentException("Invalid gateId.", nameof(gateId));

            await _repo.DeleteOpenedBlockSnapshotsByGateIdAsync(gateId);
        }

        private async Task ApplyLocationReferencesAsync(GateStructure gateStructure, GateStructureDto gateStructureDto, bool isCreate = false)
        {
            gateStructure.LocationId = await ResolveLocationReferenceAsync(
                gateStructureDto.LocationId,
                gateStructureDto.Location,
                "Location");

            gateStructure.AnchorPointId = await ResolveLocationReferenceAsync(
                gateStructureDto.AnchorPointId,
                gateStructureDto.AnchorPoint,
                "AnchorPoint");

            gateStructure.OpenAnchorPointId = await ResolveLocationReferenceAsync(
                gateStructureDto.OpenAnchorPointId,
                gateStructureDto.OpenAnchorPoint,
                "OpenAnchorPoint");

            gateStructure.ReferencePoint1Id = await ResolveLocationReferenceAsync(
                gateStructureDto.ReferencePoint1Id,
                gateStructureDto.ReferencePoint1,
                "ReferencePoint1");

            gateStructure.ReferencePoint2Id = await ResolveLocationReferenceAsync(
                gateStructureDto.ReferencePoint2Id,
                gateStructureDto.ReferencePoint2,
                "ReferencePoint2");

            gateStructure.HingeAxisId = await ResolveLocationReferenceAsync(
                gateStructureDto.HingeAxisId,
                gateStructureDto.HingeAxis,
                "HingeAxis");

            gateStructure.LeftDoorSeedBlockId = await ResolveLocationReferenceAsync(
                gateStructureDto.LeftDoorSeedBlockId,
                gateStructureDto.LeftDoorSeedBlock,
                "LeftDoorSeedBlock");

            gateStructure.RightDoorSeedBlockId = await ResolveLocationReferenceAsync(
                gateStructureDto.RightDoorSeedBlockId,
                gateStructureDto.RightDoorSeedBlock,
                "RightDoorSeedBlock");

            gateStructure.InfoDisplayLocationId = await ResolveLocationReferenceAsync(
                gateStructureDto.InfoDisplayLocationId,
                gateStructureDto.InfoDisplayLocation,
                "InfoDisplayLocation");

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
