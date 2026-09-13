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
    public class GateDoorService : IGateDoorService
    {
        private readonly IGateDoorRepository _repo;
        private readonly IGateStructureRepository _structureRepo;
        private readonly ILocationRepository _locationRepo;
        private readonly ILocationService _locationService;
        private readonly IMapper _mapper;

        public GateDoorService(
            IGateDoorRepository repo,
            IGateStructureRepository structureRepo,
            ILocationRepository locationRepo,
            ILocationService locationService,
            IMapper mapper)
        {
            _repo = repo;
            _structureRepo = structureRepo;
            _locationRepo = locationRepo;
            _locationService = locationService;
            _mapper = mapper;
        }

        public async Task<IEnumerable<GateDoorDto>> GetByStructureIdAsync(int gateStructureId)
        {
            if (gateStructureId <= 0)
                throw new ArgumentException("Invalid gateStructureId.", nameof(gateStructureId));

            var doors = await _repo.GetByStructureIdAsync(gateStructureId);
            return _mapper.Map<IEnumerable<GateDoorDto>>(doors);
        }

        public async Task<GateDoorDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var door = await _repo.GetByIdAsync(id);
            return _mapper.Map<GateDoorDto>(door);
        }

        public async Task<GateDoorDto?> GetByIdWithSnapshotsAsync(int id)
        {
            if (id <= 0) return null;
            var door = await _repo.GetByIdWithSnapshotsAsync(id);
            return _mapper.Map<GateDoorDto>(door);
        }

        public async Task<GateDoorDto> CreateAsync(int gateStructureId, GateDoorDto gateDoorDto)
        {
            if (gateDoorDto == null)
                throw new ArgumentNullException(nameof(gateDoorDto));
            if (gateStructureId <= 0)
                throw new ArgumentException("Invalid gateStructureId.", nameof(gateStructureId));
            if (string.IsNullOrWhiteSpace(gateDoorDto.Name))
                throw new ArgumentException("GateDoor name is required.", nameof(gateDoorDto));

            var parentStructure = await _structureRepo.GetByIdAsync(gateStructureId);
            if (parentStructure == null)
                throw new KeyNotFoundException($"GateStructure with id {gateStructureId} not found.");

            // Name is unique within its parent structure, not globally (decision 5.0-D).
            if (!await _repo.IsDoorNameUniqueAsync(gateStructureId, gateDoorDto.Name))
                throw new ArgumentException($"A door named '{gateDoorDto.Name}' already exists on this gate structure.", nameof(gateDoorDto));

            ValidateHealth(gateDoorDto);

            var gateDoor = _mapper.Map<GateDoor>(gateDoorDto);
            gateDoor.GateStructureId = gateStructureId;
            await ApplyLocationReferencesAsync(gateDoor, gateDoorDto, isCreate: true);
            await _repo.AddAsync(gateDoor);
            return _mapper.Map<GateDoorDto>(gateDoor);
        }

        public async Task UpdateAsync(int id, GateDoorDto gateDoorDto)
        {
            if (gateDoorDto == null)
                throw new ArgumentNullException(nameof(gateDoorDto));
            if (id <= 0)
                throw new ArgumentException("Invalid id.", nameof(id));
            if (string.IsNullOrWhiteSpace(gateDoorDto.Name))
                throw new ArgumentException("GateDoor name is required.", nameof(gateDoorDto));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null)
                throw new KeyNotFoundException($"GateDoor with id {id} not found.");

            if (!await _repo.IsDoorNameUniqueAsync(existing.GateStructureId, gateDoorDto.Name, excludeId: id))
                throw new ArgumentException($"A door named '{gateDoorDto.Name}' already exists on this gate structure.", nameof(gateDoorDto));

            ValidateHealth(gateDoorDto);

            _mapper.Map(gateDoorDto, existing);
            await ApplyLocationReferencesAsync(existing, gateDoorDto);
            await _repo.UpdateAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0)
                throw new ArgumentException("Invalid id.", nameof(id));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null)
                throw new KeyNotFoundException($"GateDoor with id {id} not found.");

            // Delete associated block snapshots first (also DB-cascaded, kept explicit to match
            // the established GateStructureService.DeleteAsync pattern).
            await _repo.DeleteBlockSnapshotsByDoorIdAsync(id);
            await _repo.DeleteOpenedBlockSnapshotsByDoorIdAsync(id);

            await _repo.DeleteAsync(id);
        }

        public async Task UpdateHealthAsync(int id, double newHealth)
        {
            if (id <= 0)
                throw new ArgumentException("Invalid id.", nameof(id));
            if (newHealth < 0)
                throw new ArgumentException("Health cannot be negative.", nameof(newHealth));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null)
                throw new KeyNotFoundException($"GateDoor with id {id} not found.");

            if (newHealth > existing.HealthMax)
                throw new ArgumentException($"Health cannot exceed HealthMax ({existing.HealthMax}).", nameof(newHealth));

            await _repo.UpdateHealthAsync(id, newHealth);
        }

        public async Task UpdateStateAsync(int id, GateDoorOpenState openedState, bool isDestroyed)
        {
            if (id <= 0)
                throw new ArgumentException("Invalid id.", nameof(id));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null)
                throw new KeyNotFoundException($"GateDoor with id {id} not found.");

            await _repo.UpdateStateAsync(id, openedState, isDestroyed);
        }

        public async Task UpdateOperationalSettingsAsync(int id, bool isActive, bool isInvincible)
        {
            if (id <= 0)
                throw new ArgumentException("Invalid id.", nameof(id));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null)
                throw new KeyNotFoundException($"GateDoor with id {id} not found.");

            await _repo.UpdateOperationalSettingsAsync(id, isActive, isInvincible);
        }

        public async Task UpdateRegionDataAsync(int id, bool isOpenedRegion, string regionData)
        {
            if (id <= 0)
                throw new ArgumentException("Invalid id.", nameof(id));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null)
                throw new KeyNotFoundException($"GateDoor with id {id} not found.");

            await _repo.UpdateRegionDataAsync(id, isOpenedRegion, regionData ?? string.Empty);
        }

        public async Task<IEnumerable<GateBlockSnapshotDto>> GetBlockSnapshotsAsync(int gateDoorId)
        {
            if (gateDoorId <= 0)
                throw new ArgumentException("Invalid gateDoorId.", nameof(gateDoorId));

            var snapshots = await _repo.GetBlockSnapshotsByDoorIdAsync(gateDoorId);
            return _mapper.Map<IEnumerable<GateBlockSnapshotDto>>(snapshots);
        }

        public async Task AddBlockSnapshotsAsync(int gateDoorId, IEnumerable<GateBlockSnapshotDto> snapshots)
        {
            if (gateDoorId <= 0)
                throw new ArgumentException("Invalid gateDoorId.", nameof(gateDoorId));
            if (snapshots == null || !snapshots.Any())
                throw new ArgumentException("Snapshots collection cannot be null or empty.", nameof(snapshots));

            var existing = await _repo.GetByIdAsync(gateDoorId);
            if (existing == null)
                throw new KeyNotFoundException($"GateDoor with id {gateDoorId} not found.");

            var snapshotEntities = _mapper.Map<IEnumerable<GateBlockSnapshot>>(snapshots);

            foreach (var snapshot in snapshotEntities)
            {
                snapshot.GateDoorId = gateDoorId;
            }

            await _repo.AddBlockSnapshotsAsync(snapshotEntities);
        }

        public async Task AddBlockSnapshotsAsync(int gateDoorId, IEnumerable<GateBlockSnapshotCreateDto> snapshots)
        {
            if (gateDoorId <= 0)
                throw new ArgumentException("Invalid gateDoorId.", nameof(gateDoorId));
            if (snapshots == null || !snapshots.Any())
                throw new ArgumentException("Snapshots collection cannot be null or empty.", nameof(snapshots));

            var existing = await _repo.GetByIdAsync(gateDoorId);
            if (existing == null)
                throw new KeyNotFoundException($"GateDoor with id {gateDoorId} not found.");

            var snapshotEntities = _mapper.Map<IEnumerable<GateBlockSnapshot>>(snapshots);

            foreach (var snapshot in snapshotEntities)
            {
                snapshot.GateDoorId = gateDoorId;
            }

            await _repo.AddBlockSnapshotsAsync(snapshotEntities);
        }

        public async Task ClearBlockSnapshotsAsync(int gateDoorId)
        {
            if (gateDoorId <= 0)
                throw new ArgumentException("Invalid gateDoorId.", nameof(gateDoorId));

            await _repo.DeleteBlockSnapshotsByDoorIdAsync(gateDoorId);
        }

        public async Task<IEnumerable<GateOpenedBlockSnapshotDto>> GetOpenedBlockSnapshotsAsync(int gateDoorId)
        {
            if (gateDoorId <= 0)
                throw new ArgumentException("Invalid gateDoorId.", nameof(gateDoorId));

            var snapshots = await _repo.GetOpenedBlockSnapshotsByDoorIdAsync(gateDoorId);
            return _mapper.Map<IEnumerable<GateOpenedBlockSnapshotDto>>(snapshots);
        }

        public async Task AddOpenedBlockSnapshotsAsync(int gateDoorId, IEnumerable<GateOpenedBlockSnapshotDto> snapshots)
        {
            if (gateDoorId <= 0)
                throw new ArgumentException("Invalid gateDoorId.", nameof(gateDoorId));
            if (snapshots == null || !snapshots.Any())
                throw new ArgumentException("Snapshots collection cannot be null or empty.", nameof(snapshots));

            var existing = await _repo.GetByIdAsync(gateDoorId);
            if (existing == null)
                throw new KeyNotFoundException($"GateDoor with id {gateDoorId} not found.");

            var snapshotEntities = _mapper.Map<IEnumerable<GateOpenedBlockSnapshot>>(snapshots);

            foreach (var snapshot in snapshotEntities)
            {
                snapshot.GateDoorId = gateDoorId;
            }

            await _repo.AddOpenedBlockSnapshotsAsync(snapshotEntities);
        }

        public async Task AddOpenedBlockSnapshotsAsync(int gateDoorId, IEnumerable<GateOpenedBlockSnapshotCreateDto> snapshots)
        {
            if (gateDoorId <= 0)
                throw new ArgumentException("Invalid gateDoorId.", nameof(gateDoorId));
            if (snapshots == null || !snapshots.Any())
                throw new ArgumentException("Snapshots collection cannot be null or empty.", nameof(snapshots));

            var existing = await _repo.GetByIdAsync(gateDoorId);
            if (existing == null)
                throw new KeyNotFoundException($"GateDoor with id {gateDoorId} not found.");

            var snapshotEntities = _mapper.Map<IEnumerable<GateOpenedBlockSnapshot>>(snapshots);

            foreach (var snapshot in snapshotEntities)
            {
                snapshot.GateDoorId = gateDoorId;
            }

            await _repo.AddOpenedBlockSnapshotsAsync(snapshotEntities);
        }

        public async Task ClearOpenedBlockSnapshotsAsync(int gateDoorId)
        {
            if (gateDoorId <= 0)
                throw new ArgumentException("Invalid gateDoorId.", nameof(gateDoorId));

            await _repo.DeleteOpenedBlockSnapshotsByDoorIdAsync(gateDoorId);
        }

        private static void ValidateHealth(GateDoorDto dto)
        {
            if (dto.HealthCurrent.HasValue && dto.HealthMax.HasValue &&
                dto.HealthCurrent.Value > dto.HealthMax.Value)
            {
                throw new ArgumentException("HealthCurrent cannot exceed HealthMax.", nameof(dto));
            }
        }

        private async Task ApplyLocationReferencesAsync(GateDoor gateDoor, GateDoorDto gateDoorDto, bool isCreate = false)
        {
            gateDoor.AnchorPointId = await ResolveLocationReferenceAsync(
                gateDoorDto.AnchorPointId,
                gateDoorDto.AnchorPoint,
                "AnchorPoint");

            gateDoor.OpenAnchorPointId = await ResolveLocationReferenceAsync(
                gateDoorDto.OpenAnchorPointId,
                gateDoorDto.OpenAnchorPoint,
                "OpenAnchorPoint");

            gateDoor.ReferencePoint1Id = await ResolveLocationReferenceAsync(
                gateDoorDto.ReferencePoint1Id,
                gateDoorDto.ReferencePoint1,
                "ReferencePoint1");

            gateDoor.ReferencePoint2Id = await ResolveLocationReferenceAsync(
                gateDoorDto.ReferencePoint2Id,
                gateDoorDto.ReferencePoint2,
                "ReferencePoint2");

            gateDoor.HingeAxisId = await ResolveLocationReferenceAsync(
                gateDoorDto.HingeAxisId,
                gateDoorDto.HingeAxis,
                "HingeAxis");

            gateDoor.LeftDoorSeedBlockId = await ResolveLocationReferenceAsync(
                gateDoorDto.LeftDoorSeedBlockId,
                gateDoorDto.LeftDoorSeedBlock,
                "LeftDoorSeedBlock");

            gateDoor.RightDoorSeedBlockId = await ResolveLocationReferenceAsync(
                gateDoorDto.RightDoorSeedBlockId,
                gateDoorDto.RightDoorSeedBlock,
                "RightDoorSeedBlock");

            gateDoor.InfoDisplayLocationId = await ResolveLocationReferenceAsync(
                gateDoorDto.InfoDisplayLocationId,
                gateDoorDto.InfoDisplayLocation,
                "InfoDisplayLocation");
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
