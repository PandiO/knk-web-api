using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;

namespace knkwebapi_v2.Services
{
    public class PermissionGrantService : IPermissionGrantService
    {
        private readonly IPermissionGrantRepository _repo;
        private readonly IMapper _mapper;

        public PermissionGrantService(IPermissionGrantRepository repo, IMapper mapper)
        {
            _repo = repo;
            _mapper = mapper;
        }

        public async Task<IEnumerable<PermissionGrantDto>> GetAllAsync()
        {
            var grants = await _repo.GetAllAsync();
            return _mapper.Map<IEnumerable<PermissionGrantDto>>(grants);
        }

        public async Task<PermissionGrantDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var grant = await _repo.GetByIdAsync(id);
            return _mapper.Map<PermissionGrantDto>(grant);
        }

        public async Task<PermissionGrantDto> CreateAsync(PermissionGrantDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.Node)) throw new ArgumentException("Permission node is required.", nameof(dto));
            if (dto.HolderId <= 0 || !await _repo.HolderExistsAsync(dto.HolderId))
                throw new ArgumentException($"PermissionHolder with id {dto.HolderId} not found.", nameof(dto));

            var grant = _mapper.Map<PermissionGrant>(dto);
            await _repo.AddAsync(grant);
            return _mapper.Map<PermissionGrantDto>(grant);
        }

        public async Task UpdateAsync(int id, PermissionGrantDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (string.IsNullOrWhiteSpace(dto.Node)) throw new ArgumentException("Permission node is required.", nameof(dto));

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"PermissionGrant with id {id} not found.");

            if (dto.HolderId != existing.HolderId)
            {
                if (dto.HolderId <= 0 || !await _repo.HolderExistsAsync(dto.HolderId))
                    throw new ArgumentException($"PermissionHolder with id {dto.HolderId} not found.", nameof(dto));
            }

            existing.HolderId = dto.HolderId;
            existing.Node = dto.Node;
            existing.Value = dto.Value;
            existing.ExpiresAt = dto.ExpiresAt;

            await _repo.UpdateAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"PermissionGrant with id {id} not found.");
            await _repo.DeleteAsync(id);
        }

        public async Task<PagedResultDto<PermissionGrantListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));
            var query = _mapper.Map<PagedQuery>(queryDto);
            var result = await _repo.SearchAsync(query);
            return _mapper.Map<PagedResultDto<PermissionGrantListDto>>(result);
        }
    }
}
