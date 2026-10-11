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
    public class TownService : ITownService
    {
        private readonly ITownRepository _repo;
        private readonly ILocationRepository _locationRepo;
        private readonly ILocationService _locationService;
        private readonly IMapper _mapper;
        private readonly ITeleportDestinationService _teleportDestinations;
        private readonly IDomainRegionNameFinalizer _regionNames;
        private readonly IDomainWorldResolver _worlds;

        public TownService(
            ITownRepository repo,
            ILocationRepository locationRepo,
            ILocationService locationService,
            IMapper mapper,
            ITeleportDestinationService teleportDestinations,
            IDomainRegionNameFinalizer regionNames,
            IDomainWorldResolver worlds)
        {
            _repo = repo;
            _locationRepo = locationRepo;
            _locationService = locationService;
            _mapper = mapper;
            _teleportDestinations = teleportDestinations;
            _regionNames = regionNames;
            _worlds = worlds;
        }

        public async Task<IEnumerable<TownDto>> GetAllAsync()
        {
            var towns = await _repo.GetAllAsync();
            return _mapper.Map<IEnumerable<TownDto>>(towns);
        }

        public async Task<TownDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var town = await _repo.GetByIdAsync(id);
            return _mapper.Map<TownDto>(town);
        }

        public async Task<TownDto> CreateAsync(TownDto townDto)
        {
            if (townDto == null) throw new ArgumentNullException(nameof(townDto));
            if (string.IsNullOrWhiteSpace(townDto.Name)) throw new ArgumentException("Town name is required.", nameof(townDto));
            if (string.IsNullOrWhiteSpace(townDto.WgRegionId)) throw new ArgumentException("WgRegionId is required.", nameof(townDto));
            await _teleportDestinations.ValidateSettingsAsync(townDto);
            // KNG-111: before the Location is saved, so a rejected world leaves nothing behind.
            string worldName = await _worlds.ResolveAsync(WorldRequest(townDto, null));

            // Handle nested Location entity
            int? resolvedLocationId = await HandleLocationAsync(townDto);

            var town = _mapper.Map<Town>(townDto);
            town.LocationId = resolvedLocationId;
            town.WorldName = worldName;
            DomainTeleportSettings.Apply(town, townDto);
            DomainNavigationDefaults.Apply(town, townDto);
            town.CreatedAt = DateTime.UtcNow;
            await _repo.AddTownAsync(town);
            await _regionNames.FinalizeAsync(town);
            return _mapper.Map<TownDto>(town);
        }

        public async Task UpdateAsync(int id, TownDto townDto)
        {
            if (townDto == null) throw new ArgumentNullException(nameof(townDto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (string.IsNullOrWhiteSpace(townDto.Name)) throw new ArgumentException("Town name is required.", nameof(townDto));
            if (string.IsNullOrWhiteSpace(townDto.WgRegionId)) throw new ArgumentException("WgRegionId is required.", nameof(townDto));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"Town with id {id} not found.");
            await _teleportDestinations.ValidateSettingsAsync(townDto);
            string worldName = await _worlds.ResolveAsync(WorldRequest(townDto, id));

            // Handle nested Location entity
            int? resolvedLocationId = await HandleLocationAsync(townDto);

            existing.Name = townDto.Name;
            existing.WorldName = worldName;
            existing.Description = townDto.Description;
            existing.AllowEntry = townDto.AllowEntry ?? true;
            existing.AllowExit = townDto.AllowExit ?? true;
            existing.WgRegionId = townDto.WgRegionId;
            existing.LocationId = resolvedLocationId;
            DomainTeleportSettings.Apply(existing, townDto);
            DomainNavigationDefaults.Apply(existing, townDto);

            await _repo.UpdateTownAsync(existing);
            await _regionNames.FinalizeAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"Town with id {id} not found.");

            await _repo.DeleteTownAsync(id);
        }

        public async Task<PagedResultDto<TownListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

            var query = _mapper.Map<PagedQuery>(queryDto);
            var result = await _repo.SearchAsync(query);
            var resultDto = _mapper.Map<PagedResultDto<TownListDto>>(result);

            return resultDto;
        }

        private static DomainWorldRequest WorldRequest(TownDto dto, int? id) => new()
        {
            DomainId = id,
            RequestedWorld = dto.WorldName,
            WgRegionId = dto.WgRegionId,
            LocationWorld = dto.Location?.World,
            LocationId = dto.Location?.Id ?? dto.LocationId
        };

        /// <summary>
        /// Handles Location creation/update logic.
        /// Returns the LocationId to be assigned to Town.
        /// </summary>
        private async Task<int?> HandleLocationAsync(TownDto townDto)
        {
            // Case 1: No location provided at all
            if (townDto.Location == null && !townDto.LocationId.HasValue)
            {
                return null;
            }

            // Case 2: Only LocationId provided (reference to existing)
            if (townDto.Location == null && townDto.LocationId.HasValue)
            {
                var locationExists = await _locationRepo.GetByIdAsync(townDto.LocationId.Value);
                if (locationExists == null)
                    throw new ArgumentException($"Location with id {townDto.LocationId} not found.");
                return townDto.LocationId;
            }

            // Case 3: Location object provided (new or existing)
            if (townDto.Location != null)
            {
                // Check if Location is new (no ID) or existing (has ID)
                if (!townDto.Location.Id.HasValue || townDto.Location.Id == 0)
                {
                    // NEW LOCATION: Create it first
                    var createdLocation = await _locationService.CreateAsync(townDto.Location);
                    return createdLocation.Id;
                }
                else
                {
                    // EXISTING LOCATION: Update it
                    await _locationService.UpdateAsync(townDto.Location.Id.Value, townDto.Location);
                    return townDto.Location.Id;
                }
            }

            return null;
        }
    }
}
