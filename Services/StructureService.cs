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
        private readonly IMapper _mapper;
        private readonly ITeleportDestinationService _teleportDestinations;
        private readonly IDomainRegionNameFinalizer _regionNames;

        public StructureService(
            IStructureRepository repo,
            IStreetRepository streetRepo,
            IDistrictRepository districtRepo,
            ILocationRepository locationRepo,
            IMapper mapper,
            ITeleportDestinationService teleportDestinations,
            IDomainRegionNameFinalizer regionNames)
        {
            _repo = repo;
            _streetRepo = streetRepo;
            _districtRepo = districtRepo;
            _locationRepo = locationRepo;
            _mapper = mapper;
            _teleportDestinations = teleportDestinations;
            _regionNames = regionNames;
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
            if (string.IsNullOrWhiteSpace(structureDto.Name)) throw new ArgumentException("Structure name is required.", nameof(structureDto));
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

            // Validate LocationId if provided
            if (structureDto.LocationId.HasValue)
            {
                var location = await _locationRepo.GetByIdAsync(structureDto.LocationId.Value);
                if (location == null)
                    throw new ArgumentException($"Location with id {structureDto.LocationId} not found.", nameof(structureDto));
            }

            var structure = _mapper.Map<Structure>(structureDto);
            structure.CreatedAt = DateTime.UtcNow;
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
            if (string.IsNullOrWhiteSpace(structureDto.Name)) throw new ArgumentException("Structure name is required.", nameof(structureDto));
            if (string.IsNullOrWhiteSpace(structureDto.WgRegionId)) throw new ArgumentException("WgRegionId is required.", nameof(structureDto));
            if (structureDto.StreetId <= 0) throw new ArgumentException("StreetId is required.", nameof(structureDto));
            if (structureDto.DistrictId <= 0) throw new ArgumentException("DistrictId is required.", nameof(structureDto));
            if (structureDto.HouseNumber <= 0) throw new ArgumentException("HouseNumber must be greater than 0.", nameof(structureDto));
            
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"Structure with id {id} not found.");
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

            // Validate LocationId if provided
            if (structureDto.LocationId.HasValue)
            {
                var location = await _locationRepo.GetByIdAsync(structureDto.LocationId.Value);
                if (location == null)
                    throw new ArgumentException($"Location with id {structureDto.LocationId} not found.", nameof(structureDto));
            }

            existing.Name = structureDto.Name;
            existing.Description = structureDto.Description;
            existing.AllowEntry = structureDto.AllowEntry ?? true;
            existing.AllowExit = structureDto.AllowExit ?? true;
            existing.WgRegionId = structureDto.WgRegionId;
            existing.LocationId = structureDto.LocationId;
            existing.StreetId = structureDto.StreetId;
            existing.DistrictId = structureDto.DistrictId;
            existing.HouseNumber = structureDto.HouseNumber;
            DomainTeleportSettings.Apply(existing, structureDto);
            DomainNavigationDefaults.Apply(existing, structureDto);

            await _repo.UpdateStructureAsync(existing);
            await _regionNames.FinalizeAsync(existing);
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
    }
}
