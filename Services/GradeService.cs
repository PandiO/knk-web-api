using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    public class GradeService : IGradeService
    {
        private readonly IGradeRepository _repo;
        private readonly IMapper _mapper;

        public GradeService(IGradeRepository repo, IMapper mapper)
        {
            _repo = repo;
            _mapper = mapper;
        }

        public async Task<IEnumerable<GradeReadDto>> GetAllAsync()
        {
            var items = await _repo.GetAllAsync();
            return _mapper.Map<IEnumerable<GradeReadDto>>(items);
        }

        public async Task<GradeReadDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var entity = await _repo.GetByIdAsync(id);
            return _mapper.Map<GradeReadDto>(entity);
        }

        public async Task<GradeReadDto> CreateAsync(GradeCreateDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Name is required.", nameof(dto));
            Validate(dto.DropChance, dto.EnchantLevelCapDivisor);

            var entity = _mapper.Map<Grade>(dto);
            await _repo.AddAsync(entity);
            return _mapper.Map<GradeReadDto>(entity);
        }

        public async Task UpdateAsync(int id, GradeUpdateDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Name is required.", nameof(dto));
            Validate(dto.DropChance, dto.EnchantLevelCapDivisor);

            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"Grade with id {id} not found.");

            existing.Name = dto.Name;
            existing.Stars = dto.Stars;
            existing.DropChance = dto.DropChance;
            existing.EnchantLevelCapDivisor = dto.EnchantLevelCapDivisor;

            await _repo.UpdateAsync(existing);
        }

        // Same bounds as the DTOs' [Range]s, for callers that skip model validation. A divisor below 1 would
        // divide by zero (or flip the sign of) knk-plugin's enchant-book level cap.
        private static void Validate(decimal? dropChance, int? enchantLevelCapDivisor)
        {
            if (dropChance is < 0 or > 100)
                throw new ArgumentException("DropChance must be between 0 and 100 (percent).", nameof(dropChance));
            if (enchantLevelCapDivisor is < 1)
                throw new ArgumentException("EnchantLevelCapDivisor must be at least 1, or empty for uncapped.", nameof(enchantLevelCapDivisor));
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id);
            if (existing == null) throw new KeyNotFoundException($"Grade with id {id} not found.");

            await _repo.DeleteAsync(id);
        }

        public async Task<PagedResultDto<GradeListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

            var query = _mapper.Map<PagedQuery>(queryDto);
            var result = await _repo.SearchAsync(query);
            return _mapper.Map<PagedResultDto<GradeListDto>>(result);
        }
    }
}
