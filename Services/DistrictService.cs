using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Services
{
    public class DistrictService : IDistrictService
    {
        private readonly IDistrictRepository _repo;
        private readonly ITownRepository _townRepo;
        private readonly ILocationRepository _locationRepo;
        private readonly IStreetRepository _streetRepo;
        private readonly IMapper _mapper;
        private readonly IRegionService _regionService;
        private readonly ILogger<DistrictService> _logger;
        private readonly ITeleportDestinationService _teleportDestinations;

        public DistrictService(
            IDistrictRepository repo,
            ITownRepository townRepo,
            ILocationRepository locationRepo,
            IStreetRepository streetRepo,
            IMapper mapper,
            IRegionService regionService,
            ILogger<DistrictService> logger,
            ITeleportDestinationService teleportDestinations)
        {
            _repo = repo;
            _townRepo = townRepo;
            _locationRepo = locationRepo;
            _streetRepo = streetRepo;
            _mapper = mapper;
            _regionService = regionService;
            _logger = logger;
            _teleportDestinations = teleportDestinations;
        }

        public async Task<IEnumerable<DistrictDto>> GetAllAsync()
        {
            var districts = await _repo.GetAllAsync();
            return _mapper.Map<IEnumerable<DistrictDto>>(districts);
        }

        public async Task<DistrictDto?> GetByIdAsync(int id, string? townFields = null, string? structureFields = null, string? streetFields = null)
        {
            if (id <= 0) return null;
            var district = await _repo.GetByIdAsync(id);
            var dto = _mapper.Map<DistrictDto>(district);
            if (dto == null) return null;

            // Optionally shape embedded Town fields
            if (dto.Town != null && !string.IsNullOrWhiteSpace(townFields))
            {
                var requested = new HashSet<string>(townFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
                if (!requested.Contains("Id")) dto.Town.Id = null;
                if (!requested.Contains("Name")) dto.Town.Name = null;
                if (!requested.Contains("Description")) dto.Town.Description = null;
                if (!requested.Contains("AllowEntry")) dto.Town.AllowEntry = null;
                if (!requested.Contains("AllowExit")) dto.Town.AllowExit = null;
                if (!requested.Contains("WgRegionId")) dto.Town.WgRegionId = null;
                if (!requested.Contains("LocationId")) dto.Town.LocationId = null;
            }

            // Optionally shape embedded Streets fields
            if (dto.Streets != null && !string.IsNullOrWhiteSpace(streetFields))
            {
                var requested = new HashSet<string>(streetFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
                dto.Streets = dto.Streets.Select(s => {
                    var shaped = new DistrictStreetDto { };
                    if (requested.Contains("Id")) shaped.Id = s.Id;
                    if (requested.Contains("Name")) shaped.Name = s.Name;
                    return shaped;
                }).ToList();
            }

            // Optionally shape embedded Structures fields
            if (dto.Structures != null && !string.IsNullOrWhiteSpace(structureFields))
            {
                var requested = new HashSet<string>(structureFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
                dto.Structures = dto.Structures.Select(s => {
                    var shaped = new DistrictStructureDto { };
                    if (requested.Contains("Id")) shaped.Id = s.Id;
                    if (requested.Contains("Name")) shaped.Name = s.Name;
                    if (requested.Contains("Description")) shaped.Description = s.Description;
                    if (requested.Contains("HouseNumber")) shaped.HouseNumber = s.HouseNumber;
                    if (requested.Contains("StreetId")) shaped.StreetId = s.StreetId;
                    return shaped;
                }).ToList();
            }

            return dto;
        }

        public async Task<DistrictDto> CreateAsync(DistrictDto districtDto)
        {
            if (districtDto == null) throw new ArgumentNullException(nameof(districtDto));
            if (string.IsNullOrWhiteSpace(districtDto.Name)) throw new ArgumentException("District name is required.", nameof(districtDto));
            if (string.IsNullOrWhiteSpace(districtDto.WgRegionId)) throw new ArgumentException("WgRegionId is required.", nameof(districtDto));
            if (districtDto.TownId <= 0) throw new ArgumentException("TownId is required.", nameof(districtDto));

            // Validate that Town exists
            var town = await _townRepo.GetByIdAsync(districtDto.TownId);
            if (town == null) throw new ArgumentException($"Town with id {districtDto.TownId} not found.", nameof(districtDto));
            await _teleportDestinations.ValidateSettingsAsync(districtDto);
            // Cascade create/update for Location if embedded payload provided
            if (districtDto.Location != null)
            {
                if (districtDto.Location.Id.HasValue && districtDto.Location.Id.Value > 0)
                {
                    var existingLoc = await _locationRepo.GetByIdAsync(districtDto.Location.Id.Value);
                    if (existingLoc == null)
                        throw new ArgumentException($"Location with id {districtDto.Location.Id.Value} not found.", nameof(districtDto));
                    // Update fields
                    existingLoc.Name = districtDto.Location.Name ?? existingLoc.Name;
                    existingLoc.X = districtDto.Location.X.HasValue ? districtDto.Location.X.Value : existingLoc.X;
                    existingLoc.Y = districtDto.Location.Y.HasValue ? districtDto.Location.Y.Value : existingLoc.Y;
                    existingLoc.Z = districtDto.Location.Z.HasValue ? districtDto.Location.Z.Value : existingLoc.Z;
                    existingLoc.Yaw = districtDto.Location.Yaw.HasValue ? districtDto.Location.Yaw.Value : existingLoc.Yaw;
                    existingLoc.Pitch = districtDto.Location.Pitch.HasValue ? districtDto.Location.Pitch.Value : existingLoc.Pitch;
                    existingLoc.World = districtDto.Location.World ?? existingLoc.World;
                    await _locationRepo.UpdateLocationAsync(existingLoc);
                    districtDto.LocationId = existingLoc.Id;
                }
                else
                {
                    var newLoc = new Location
                    {
                        Name = districtDto.Location.Name ?? string.Empty,
                        X = districtDto.Location.X.HasValue ? districtDto.Location.X.Value : 0,
                        Y = districtDto.Location.Y.HasValue ? districtDto.Location.Y.Value : 0,
                        Z = districtDto.Location.Z.HasValue ? districtDto.Location.Z.Value : 0,
                        Yaw = districtDto.Location.Yaw.HasValue ? districtDto.Location.Yaw.Value : 0,
                        Pitch = districtDto.Location.Pitch.HasValue ? districtDto.Location.Pitch.Value : 0,
                        World = districtDto.Location.World ?? string.Empty,
                    };
                    await _locationRepo.AddLocationAsync(newLoc);
                    districtDto.LocationId = newLoc.Id;
                }
            }
            else if (districtDto.LocationId.HasValue)
            {
                // Validate referenced location exists
                var location = await _locationRepo.GetByIdAsync(districtDto.LocationId.Value);
                if (location == null)
                    throw new ArgumentException($"Location with id {districtDto.LocationId} not found.", nameof(districtDto));
            }

            var district = _mapper.Map<District>(districtDto);
            district.CreatedAt = DateTime.UtcNow;
            DomainTeleportSettings.Apply(district, districtDto);

            // Handle Street relationships
            if (districtDto.StreetIds != null && districtDto.StreetIds.Any())
            {
                district.Streets = new Collection<Street>();
                foreach (var streetId in districtDto.StreetIds)
                {
                    var street = await _streetRepo.GetByIdAsync(streetId);
                    if (street == null)
                        throw new ArgumentException($"Street with id {streetId} not found.", nameof(districtDto));
                    district.Streets.Add(street);
                }
            }

            await _repo.AddDistrictAsync(district);
            
            // After successful creation, finalize the region name if it has a temporary ID
            if (!string.IsNullOrWhiteSpace(district.WgRegionId) && district.WgRegionId.StartsWith("tempregion_worldtask_"))
            {
                await FinalizeRegionNameAsync(district);
            }
            
            return _mapper.Map<DistrictDto>(district);
        }

        public async Task UpdateAsync(int id, DistrictDto districtDto)
        {
            if (districtDto == null) throw new ArgumentNullException(nameof(districtDto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (string.IsNullOrWhiteSpace(districtDto.Name)) throw new ArgumentException("District name is required.", nameof(districtDto));
            if (string.IsNullOrWhiteSpace(districtDto.WgRegionId)) throw new ArgumentException("WgRegionId is required.", nameof(districtDto));
            if (districtDto.TownId <= 0) throw new ArgumentException("TownId is required.", nameof(districtDto));
            
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"District with id {id} not found.");
            await _teleportDestinations.ValidateSettingsAsync(districtDto);

            // Validate that Town exists if changing
            if (existing.TownId != districtDto.TownId)
            {
                var town = await _townRepo.GetByIdAsync(districtDto.TownId);
                if (town == null) throw new ArgumentException($"Town with id {districtDto.TownId} not found.", nameof(districtDto));
            }
            // Cascade create/update for Location if embedded payload provided
            if (districtDto.Location != null)
            {
                if (districtDto.Location.Id.HasValue && districtDto.Location.Id.Value > 0)
                {
                    var existingLoc = await _locationRepo.GetByIdAsync(districtDto.Location.Id.Value);
                    if (existingLoc == null)
                        throw new ArgumentException($"Location with id {districtDto.Location.Id.Value} not found.", nameof(districtDto));
                    existingLoc.Name = districtDto.Location.Name ?? existingLoc.Name;
                    existingLoc.X = districtDto.Location.X.HasValue ? districtDto.Location.X.Value : existingLoc.X;
                    existingLoc.Y = districtDto.Location.Y.HasValue ? districtDto.Location.Y.Value : existingLoc.Y;
                    existingLoc.Z = districtDto.Location.Z.HasValue ? districtDto.Location.Z.Value : existingLoc.Z;
                    existingLoc.Yaw = districtDto.Location.Yaw.HasValue ? districtDto.Location.Yaw.Value : existingLoc.Yaw;
                    existingLoc.Pitch = districtDto.Location.Pitch.HasValue ? districtDto.Location.Pitch.Value : existingLoc.Pitch;
                    existingLoc.World = districtDto.Location.World ?? existingLoc.World;
                    await _locationRepo.UpdateLocationAsync(existingLoc);
                    districtDto.LocationId = existingLoc.Id;
                }
                else
                {
                    var newLoc = new Location
                    {
                        Name = districtDto.Location.Name ?? string.Empty,
                        X = districtDto.Location.X.HasValue ? districtDto.Location.X.Value : 0,
                        Y = districtDto.Location.Y.HasValue ? districtDto.Location.Y.Value : 0,
                        Z = districtDto.Location.Z.HasValue ? districtDto.Location.Z.Value : 0,
                        Yaw = districtDto.Location.Yaw.HasValue ? districtDto.Location.Yaw.Value : 0,
                        Pitch = districtDto.Location.Pitch.HasValue ? districtDto.Location.Pitch.Value : 0,
                        World = districtDto.Location.World ?? string.Empty,
                    };
                    await _locationRepo.AddLocationAsync(newLoc);
                    districtDto.LocationId = newLoc.Id;
                }
            }
            else if (districtDto.LocationId.HasValue)
            {
                var location = await _locationRepo.GetByIdAsync(districtDto.LocationId.Value);
                if (location == null)
                    throw new ArgumentException($"Location with id {districtDto.LocationId} not found.", nameof(districtDto));
            }

            existing.Name = districtDto.Name;
            existing.Description = districtDto.Description;
            existing.AllowEntry = districtDto.AllowEntry ?? true;
            existing.AllowExit = districtDto.AllowExit ?? true;
            existing.WgRegionId = districtDto.WgRegionId;
            existing.LocationId = districtDto.LocationId;
            existing.TownId = districtDto.TownId;
            DomainTeleportSettings.Apply(existing, districtDto);

            // Handle Street relationships
            if (districtDto.StreetIds != null)
            {
                // Clear existing streets
                existing.Streets.Clear();
                // Add new streets
                foreach (var streetId in districtDto.StreetIds)
                {
                    var street = await _streetRepo.GetByIdAsync(streetId);
                    if (street == null)
                        throw new ArgumentException($"Street with id {streetId} not found.", nameof(districtDto));
                    existing.Streets.Add(street);
                }
            }

            await _repo.UpdateDistrictAsync(existing);
            
            // After successful update, finalize the region name if it has a temporary ID
            if (!string.IsNullOrWhiteSpace(districtDto.WgRegionId) && districtDto.WgRegionId.StartsWith("tempregion_worldtask_"))
            {
                await FinalizeRegionNameAsync(existing);
            }
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"District with id {id} not found.");

            await _repo.DeleteDistrictAsync(id);
        }

        public async Task<PagedResultDto<DistrictListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

            var query = _mapper.Map<PagedQuery>(queryDto);
            var result = await _repo.SearchAsync(query);
            var resultDto = _mapper.Map<PagedResultDto<DistrictListDto>>(result);

            return resultDto;
        }

        /// <summary>
        /// Finalize the region name from temporary format to the actual formatted name.
        /// Format for domain instance entities: "domain_{entity-id}"
        /// </summary>
        private async Task FinalizeRegionNameAsync(District district)
        {
            try
            {
                string finalRegionName = $"domain_{district.Id}";
                
                // Only attempt rename if the current name is temporary
                if (district.WgRegionId.StartsWith("tempregion_worldtask_"))
                {
                    _logger.LogInformation($"Finalizing region name for District {district.Id}: {district.WgRegionId} -> {finalRegionName}");
                    
                    // The plugin also sets the region up as a District: parent = its Town's region, priority, flags.
                    var town = district.TownId > 0 ? await _townRepo.GetByIdAsync(district.TownId) : null;
                    bool renameSuccess = await _regionService.RenameRegionAsync(district.WgRegionId, finalRegionName, "District", town?.WgRegionId);
                    
                    if (renameSuccess)
                    {
                        // Update the district with the new region name
                        district.WgRegionId = finalRegionName;
                        await _repo.UpdateDistrictAsync(district);
                        _logger.LogInformation($"Successfully finalized region name for District {district.Id}: {finalRegionName}");
                    }
                    else
                    {
                        _logger.LogWarning($"Failed to finalize region name for District {district.Id}: rename operation failed");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error finalizing region name for District {district.Id}: {ex.Message}");
                // Don't throw - allow the entity creation to succeed even if region renaming fails
            }
        }
    }
}
