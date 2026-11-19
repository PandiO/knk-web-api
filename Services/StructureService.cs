using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Production;

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
        private readonly ITownRepository? _townRepo;
        private readonly IStructurePlacement? _placement;

        public StructureService(
            IStructureRepository repo,
            IStreetRepository streetRepo,
            IDistrictRepository districtRepo,
            ILocationRepository locationRepo,
            ILocationService locationService,
            IMapper mapper,
            ITeleportDestinationService teleportDestinations,
            IDomainRegionNameFinalizer regionNames,
            IDomainWorldResolver worlds,
            ITownRepository? townRepo = null,
            IStructurePlacement? placement = null)
        {
            _townRepo = townRepo;
            _placement = placement;
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
            var (district, town) = await ValidatePlacementAsync(structureDto, null);
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
            List<string> warnings = await CheckPlacementRulesAsync(structure, district, town);
            await _repo.AddStructureAsync(structure);
            await _regionNames.FinalizeAsync(structure);
            var created = _mapper.Map<StructureDto>(structure);
            created.PlacementWarnings = warnings;
            return created;
        }

        public async Task UpdateAsync(int id, StructureDto structureDto)
        {
            if (structureDto == null) throw new ArgumentNullException(nameof(structureDto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            DomainNameRules.Normalize(structureDto, "Structure", nameof(structureDto));
            if (string.IsNullOrWhiteSpace(structureDto.WgRegionId)) throw new ArgumentException("WgRegionId is required.", nameof(structureDto));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"Structure with id {id} not found.");
            // KNG-103: the region this save replaces when the region step was run again
            string previousRegionId = existing.WgRegionId;
            // KNG-78: this path can also rename a GateStructure; 'here' is reserved for gates only.
            if (existing is GateStructure)
                GateNameRules.EnsureNotReserved(structureDto.Name, "gate structure", "/gate", nameof(structureDto));
            await _teleportDestinations.ValidateSettingsAsync(structureDto);

            var (district, town) = await ValidatePlacementAsync(structureDto, existing);

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
            existing.TownId = structureDto.TownId;
            existing.HouseNumber = structureDto.HouseNumber;
            DomainTeleportSettings.Apply(existing, structureDto);
            DomainNavigationDefaults.Apply(existing, structureDto);
            await CheckPlacementRulesAsync(existing, district, town);

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
            ParentDomainId = dto.DistrictId ?? dto.TownId // rural (P2, #91): the town's world
        };

        /// <summary>
        /// P2 (#91): District-xor-Town, the street and house number (normalized onto <paramref name="dto"/>: 0 = not
        /// set, a rural structure gets no street and house number 0), and that the district, town and street exist.
        /// Unchanged ids of <paramref name="existing"/> are not looked up again.
        /// </summary>
        private async Task<(District? District, Town? Town)> ValidatePlacementAsync(StructureDto dto, Structure? existing)
        {
            var n = StructurePlacement.Normalize(
                new StructurePlacementInput(dto.DistrictId, dto.TownId, dto.StreetId, dto.HouseNumber), nameof(dto));
            dto.DistrictId = n.DistrictId;
            dto.TownId = n.TownId;
            dto.StreetId = n.StreetId;
            dto.HouseNumber = n.HouseNumber;

            if (n.StreetId is int streetId && existing?.StreetId != streetId
                && await _streetRepo.GetByIdAsync(streetId) == null)
                throw new ArgumentException($"Street with id {streetId} not found.", nameof(dto));

            District? district = null;
            if (n.DistrictId is int districtId)
            {
                district = existing?.DistrictId == districtId && existing.District != null
                    ? existing.District
                    : await _districtRepo.GetByIdAsync(districtId);
                if (district == null) throw new ArgumentException($"District with id {districtId} not found.", nameof(dto));
            }

            Town? town = null;
            if (n.TownId is int townId)
            {
                town = existing?.TownId == townId && existing.Town != null
                    ? existing.Town
                    : _townRepo == null ? null : await _townRepo.GetByIdAsync(townId);
                if (town == null) throw new ArgumentException($"Town with id {townId} not found.", nameof(dto));
            }
            return (district, town);
        }

        /// <summary>Placement rules P-1 to P-3 of the default Structure form on save (DESIGN § 3.4).</summary>
        private async Task<List<string>> CheckPlacementRulesAsync(Structure structure, District? district, Town? town)
        {
            if (_placement == null) return new List<string>();
            // The rules read the selected records (District.WgRegionId, Town for the territory lookup).
            if (district != null && structure.District?.Id != district.Id) structure.District = district;
            if (district == null) structure.District = null;
            if (town != null && structure.Town?.Id != town.Id) structure.Town = town;
            if (town == null) structure.Town = null;
            return await _placement.CheckRulesAsync(nameof(Structure), structure);
        }

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
