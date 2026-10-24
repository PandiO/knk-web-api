using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    public class StructureService : IStructureService
    {
        private readonly IStructureRepository _repo;
        private readonly IStreetRepository _streetRepo;
        private readonly IDistrictRepository _districtRepo;
        private readonly ILocationRepository _locationRepo;
        private readonly ILocationService _locationService;
        private readonly IMapper _mapper;
        private readonly ITeleportDestinationService _teleportDestinations;
        private readonly IDomainRegionNameFinalizer _regionNames;
        private readonly IDomainWorldResolver _worlds;

        public StructureService(
            IStructureRepository repo,
            IStreetRepository streetRepo,
            IDistrictRepository districtRepo,
            ILocationRepository locationRepo,
            ILocationService locationService,
            IMapper mapper,
            ITeleportDestinationService teleportDestinations,
            IDomainRegionNameFinalizer regionNames,
            IDomainWorldResolver worlds)
        {
            _repo = repo;
            _streetRepo = streetRepo;
            _districtRepo = districtRepo;
            _locationRepo = locationRepo;
            _locationService = locationService;
            _mapper = mapper;
            _teleportDestinations = teleportDestinations;
            _regionNames = regionNames;
            _worlds = worlds;
        }

        public async Task<IEnumerable<StructureDto>> GetAllAsync()
        {
            var structures = await _repo.GetAllAsync();
            return _mapper.Map<IEnumerable<StructureDto>>(structures);
        }

        public async Task<StructureDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var structure = await _repo.GetByIdAsync(id);
            // Map with embedded lightweight navigations (Street, District) via mapping profile
            return _mapper.Map<StructureDto>(structure);
        }

        public async Task<StructureDto> CreateAsync(StructureDto structureDto)
        {
            if (structureDto == null) throw new ArgumentNullException(nameof(structureDto));
            DomainNameRules.Normalize(structureDto, "Structure", nameof(structureDto));
            if (string.IsNullOrWhiteSpace(structureDto.WgRegionId)) throw new ArgumentException("WgRegionId is required.", nameof(structureDto));
            if (structureDto.StreetId <= 0) throw new ArgumentException("StreetId is required.", nameof(structureDto));
            if (structureDto.DistrictId <= 0) throw new ArgumentException("DistrictId is required.", nameof(structureDto));
            if (structureDto.HouseNumber <= 0) throw new ArgumentException("HouseNumber must be greater than 0.", nameof(structureDto));

            // Validate that Street exists
            var street = await _streetRepo.GetByIdAsync(structureDto.StreetId);
            if (street == null) throw new ArgumentException($"Street with id {structureDto.StreetId} not found.", nameof(structureDto));

            // Validate that District exists
            var district = await _districtRepo.GetByIdAsync(structureDto.DistrictId);
            if (district == null) throw new ArgumentException($"District with id {structureDto.DistrictId} not found.", nameof(structureDto));
            await _teleportDestinations.ValidateSettingsAsync(structureDto);

            // KNG-111: before the Location is saved, so a rejected world leaves nothing behind.
            string worldName = await _worlds.ResolveAsync(WorldRequest(structureDto, null));
            int? locationId = await ResolveLocationAsync(structureDto);

            var structure = _mapper.Map<Structure>(structureDto);
            structure.LocationId = locationId;
            structure.CreatedAt = DateTime.UtcNow;
            structure.WorldName = worldName;
            DomainTeleportSettings.Apply(structure, structureDto);
            DomainNavigationDefaults.Apply(structure, structureDto);
            await _repo.AddStructureAsync(structure);
            await _regionNames.FinalizeAsync(structure);
            return _mapper.Map<StructureDto>(structure);
        }

        public async Task UpdateAsync(int id, StructureDto structureDto)
        {
            if (structureDto == null) throw new ArgumentNullException(nameof(structureDto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            DomainNameRules.Normalize(structureDto, "Structure", nameof(structureDto));
            if (string.IsNullOrWhiteSpace(structureDto.WgRegionId)) throw new ArgumentException("WgRegionId is required.", nameof(structureDto));
            if (structureDto.StreetId <= 0) throw new ArgumentException("StreetId is required.", nameof(structureDto));
            if (structureDto.DistrictId <= 0) throw new ArgumentException("DistrictId is required.", nameof(structureDto));
            if (structureDto.HouseNumber <= 0) throw new ArgumentException("HouseNumber must be greater than 0.", nameof(structureDto));
            
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"Structure with id {id} not found.");
            // KNG-103: the region this save replaces when the region step was run again
            string previousRegionId = existing.WgRegionId;
            // KNG-78: this path can also rename a GateStructure; 'here' is reserved for gates only.
            if (existing is GateStructure)
                GateNameRules.EnsureNotReserved(structureDto.Name, "gate structure", "/gate", nameof(structureDto));
            await _teleportDestinations.ValidateSettingsAsync(structureDto);

            // Validate that Street exists if changing
            if (existing.StreetId != structureDto.StreetId)
            {
                var street = await _streetRepo.GetByIdAsync(structureDto.StreetId);
                if (street == null) throw new ArgumentException($"Street with id {structureDto.StreetId} not found.", nameof(structureDto));
            }

            // Validate that District exists if changing
            if (existing.DistrictId != structureDto.DistrictId)
            {
                var district = await _districtRepo.GetByIdAsync(structureDto.DistrictId);
                if (district == null) throw new ArgumentException($"District with id {structureDto.DistrictId} not found.", nameof(structureDto));
            }

            string worldName = await _worlds.ResolveAsync(WorldRequest(structureDto, id));
            int? locationId = await ResolveLocationAsync(structureDto);

            existing.Name = structureDto.Name;
            existing.Description = structureDto.Description;
            existing.WorldName = worldName;
            existing.AllowEntry = structureDto.AllowEntry ?? true;
            existing.AllowExit = structureDto.AllowExit ?? true;
            existing.WgRegionId = structureDto.WgRegionId;
            existing.LocationId = locationId;
            existing.StreetId = structureDto.StreetId;
            existing.DistrictId = structureDto.DistrictId;
            existing.HouseNumber = structureDto.HouseNumber;
            DomainTeleportSettings.Apply(existing, structureDto);
            DomainNavigationDefaults.Apply(existing, structureDto);

            await _repo.UpdateStructureAsync(existing);
            await _regionNames.FinalizeAsync(existing, previousRegionId);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"Structure with id {id} not found.");

            await _repo.DeleteStructureAsync(id);
        }

        public async Task<PagedResultDto<StructureListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

            var query = _mapper.Map<PagedQuery>(queryDto);
            var result = await _repo.SearchAsync(query);
            var resultDto = _mapper.Map<PagedResultDto<StructureListDto>>(result);

            return resultDto;
        }

        private static DomainWorldRequest WorldRequest(StructureDto dto, int? id) => new()
        {
            DomainId = id,
            RequestedWorld = dto.WorldName,
            WgRegionId = dto.WgRegionId,
            LocationWorld = dto.Location?.World,
            LocationId = dto.Location?.Id ?? dto.LocationId,
            ParentDomainId = dto.DistrictId
        };

        /// <summary>
        /// KNG-195: the Structure's spawn point, as Town and GateStructure resolve theirs. An embedded
        /// Location without an id is created (LocationService refuses one without a world), one with an id
        /// is updated, and a bare locationId must exist. Returns the LocationId to store.
        /// </summary>
        private async Task<int?> ResolveLocationAsync(StructureDto dto)
        {
            if (dto.Location == null)
            {
                if (!dto.LocationId.HasValue) return null;
                var location = await _locationRepo.GetByIdAsync(dto.LocationId.Value);
                if (location == null)
                    throw new ArgumentException($"Location with id {dto.LocationId} not found.", nameof(dto));
                return dto.LocationId;
            }

            if (!dto.Location.Id.HasValue || dto.Location.Id.Value == 0)
            {
                var created = await _locationService.CreateAsync(dto.Location);
                return created.Id;
            }

            await _locationService.UpdateAsync(dto.Location.Id.Value, dto.Location);
            return dto.Location.Id.Value;
        }
    }
}
