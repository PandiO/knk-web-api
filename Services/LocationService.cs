using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;

namespace knkwebapi_v2.Services
{
    public class LocationService : ILocationService
    {
        private readonly ILocationRepository _repo;
        private readonly IMapper _mapper;

        public LocationService(ILocationRepository repo, IMapper mapper)
        {
            _repo = repo;
            _mapper = mapper;
        }

        public async Task<IEnumerable<LocationDto>> GetAllAsync()
        {
            var locations = await _repo.GetAllAsync();
            return _mapper.Map<IEnumerable<LocationDto>>(locations);
        }

        public async Task<LocationDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var location = await _repo.GetByIdAsync(id);
            return _mapper.Map<LocationDto>(location);
        }

        public async Task<LocationDto> CreateAsync(LocationDto locationDto)
        {
            if (locationDto == null) throw new ArgumentNullException(nameof(locationDto));
            if (string.IsNullOrWhiteSpace(locationDto.Name)) 
                throw new ArgumentException("Location name is required.", nameof(locationDto));
            if (string.IsNullOrWhiteSpace(locationDto.World))
                throw new ArgumentException("World is required.", nameof(locationDto));
            if (!locationDto.X.HasValue || !locationDto.Y.HasValue || !locationDto.Z.HasValue)
                throw new ArgumentException("X, Y, and Z coordinates are required to create a Location.", nameof(locationDto));

            var location = _mapper.Map<Location>(locationDto);
            location.X = locationDto.X ?? location.X;
            location.Y = locationDto.Y ?? location.Y;
            location.Z = locationDto.Z ?? location.Z;
            location.Yaw = locationDto.Yaw ?? location.Yaw;
            location.Pitch = locationDto.Pitch ?? location.Pitch;

            await _repo.AddLocationAsync(location);
            return _mapper.Map<LocationDto>(location);
        }

        public async Task UpdateAsync(int id, LocationDto locationDto)
        {
            if (locationDto == null) throw new ArgumentNullException(nameof(locationDto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (string.IsNullOrWhiteSpace(locationDto.Name)) 
                throw new ArgumentException("Location name is required.", nameof(locationDto));
            if (string.IsNullOrWhiteSpace(locationDto.World)) 
                throw new ArgumentException("World is required.", nameof(locationDto));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"Location with id {id} not found.");

            // Apply allowed updates
            existing.Name = locationDto.Name;
            existing.X = locationDto.X ?? existing.X;
            existing.Y = locationDto.Y ?? existing.Y;
            existing.Z = locationDto.Z ?? existing.Z;
            existing.Yaw = locationDto.Yaw ?? existing.Yaw;
            existing.Pitch = locationDto.Pitch ?? existing.Pitch;
            existing.World = locationDto.World;

            await _repo.UpdateLocationAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"Location with id {id} not found.");

            // KNG-118 D3 (developer decision 2026-10-12): deleting a Location never deletes the domain that uses it.
            var domains = await _repo.GetDomainsUsingAsync(id) ?? Array.Empty<string>();
            if (domains.Count > 0)
            {
                throw new LocationInUseException(
                    $"This location is the location of {string.Join(", ", domains)}. Give it another location first.");
            }

            await _repo.DeleteLocationAsync(id);
        }

        public async Task<PagedResultDto<LocationDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

            var query = _mapper.Map<PagedQuery>(queryDto);

            // Execute search
            var result = await _repo.SearchAsync(query);

            // Map result to DTO
            var resultDto = _mapper.Map<PagedResultDto<LocationDto>>(result);

            return resultDto;
        }
    }

    /// <summary>A Location a domain still uses can't be deleted (KNG-118 D3); the controller answers 409.</summary>
    public class LocationInUseException : InvalidOperationException
    {
        public LocationInUseException(string message) : base(message) { }
    }
}
