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

namespace knkwebapi_v2.Services
{
    public class StreetService : IStreetService
    {
        private readonly IStreetRepository _repo;
        private readonly IDistrictRepository _districtRepo;
        private readonly IRoadNetworkRepository _roads;
        private readonly IMapper _mapper;

        public StreetService(IStreetRepository repo, IDistrictRepository districtRepo, IRoadNetworkRepository roads, IMapper mapper)
        {
            _repo = repo;
            _districtRepo = districtRepo;
            _roads = roads;
            _mapper = mapper;
        }

        public async Task<IEnumerable<StreetDto>> GetAllAsync()
        {
            var streets = await _repo.GetAllAsync();
            var dtos = _mapper.Map<List<StreetDto>>(streets);
            // Road navigation (DESIGN §3.7): one grouped query for every street's edge counts.
            var counts = await _roads.GetStreetEdgeStatsAsync(null);
            foreach (var dto in dtos)
            {
                FillRoadCounts(dto, counts);
            }
            return dtos;
        }

        public async Task<StreetDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var street = await _repo.GetByIdAsync(id);
            if (street == null) return null;
            var dto = _mapper.Map<StreetDto>(street);
            FillRoadCounts(dto, await _roads.GetStreetEdgeStatsAsync(new[] { id }));
            return dto;
        }

        private static void FillRoadCounts(StreetDto dto, Dictionary<int, (int edgeCount, double totalLength)> counts)
        {
            if (dto.Id is int id && counts.TryGetValue(id, out var stats))
            {
                dto.EdgeCount = stats.edgeCount;
                dto.TotalLength = stats.totalLength;
            }
        }

        public async Task<StreetDto> CreateAsync(StreetDto streetDto)
        {
            if (streetDto == null) throw new ArgumentNullException(nameof(streetDto));
            if (string.IsNullOrWhiteSpace(streetDto.Name)) throw new ArgumentException("Street name is required.", nameof(streetDto));

            var street = _mapper.Map<Street>(streetDto);

                        // Handle District relationships
                        if (streetDto.DistrictIds != null && streetDto.DistrictIds.Any())
                        {
                            street.Districts = new Collection<District>();
                            foreach (var districtId in streetDto.DistrictIds)
                            {
                                var district = await _districtRepo.GetByIdAsync(districtId);
                                if (district == null)
                                    throw new ArgumentException($"District with id {districtId} not found.", nameof(streetDto));
                                street.Districts.Add(district);
                            }
                        }

            await _repo.AddStreetAsync(street);
            return _mapper.Map<StreetDto>(street);
        }

        public async Task UpdateAsync(int id, StreetDto streetDto)
        {
            if (streetDto == null) throw new ArgumentNullException(nameof(streetDto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (string.IsNullOrWhiteSpace(streetDto.Name)) throw new ArgumentException("Street name is required.", nameof(streetDto));
            
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"Street with id {id} not found.");

            existing.Name = streetDto.Name;

            // Handle District relationships
            if (streetDto.DistrictIds != null)
            {
                // Clear existing districts
                existing.Districts.Clear();
                // Add new districts
                foreach (var districtId in streetDto.DistrictIds)
                {
                    var district = await _districtRepo.GetByIdAsync(districtId);
                    if (district == null)
                        throw new ArgumentException($"District with id {districtId} not found.", nameof(streetDto));
                    existing.Districts.Add(district);
                }
            }

            await _repo.UpdateStreetAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"Street with id {id} not found.");

            await _repo.DeleteStreetAsync(id);
        }

        public async Task<PagedResultDto<StreetListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

            var query = _mapper.Map<PagedQuery>(queryDto);
            var result = await _repo.SearchAsync(query);
            var resultDto = _mapper.Map<PagedResultDto<StreetListDto>>(result);

            return resultDto;
        }
    }
}
