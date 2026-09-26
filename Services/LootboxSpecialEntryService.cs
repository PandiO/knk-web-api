using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Lootbox;

namespace knkwebapi_v2.Services
{
    public class LootboxSpecialEntryService : ILootboxSpecialEntryService
    {
        public const int MaxChancePerMillion = 1_000_000;

        private readonly ILootboxSpecialEntryRepository _repo;
        private readonly IMapper _mapper;

        public LootboxSpecialEntryService(ILootboxSpecialEntryRepository repo, IMapper mapper)
        {
            _repo = repo;
            _mapper = mapper;
        }

        public async Task<IEnumerable<LootboxSpecialEntryDto>> GetAllAsync()
        {
            return _mapper.Map<IEnumerable<LootboxSpecialEntryDto>>(await _repo.GetAllAsync());
        }

        public async Task<LootboxSpecialEntryDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var entry = await _repo.GetByIdAsync(id);
            return entry == null ? null : _mapper.Map<LootboxSpecialEntryDto>(entry);
        }

        public async Task<LootboxSpecialEntryDto> CreateAsync(LootboxSpecialEntryDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            await ValidateAsync(dto);

            var entity = _mapper.Map<LootboxSpecialEntry>(dto);
            await _repo.EnsureSpecialTagAsync(entity.ItemBlueprintId);
            await _repo.AddAsync(entity);
            return _mapper.Map<LootboxSpecialEntryDto>(await _repo.GetByIdAsync(entity.Id) ?? entity);
        }

        public async Task UpdateAsync(int id, LootboxSpecialEntryDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));

            var existing = await _repo.GetByIdAsync(id) ?? throw new KeyNotFoundException($"LootboxSpecialEntry with id {id} not found.");
            await ValidateAsync(dto);

            existing.LootboxTypeId = dto.LootboxTypeId;
            existing.ItemBlueprintId = dto.ItemBlueprintId;
            existing.ChancePerMillion = dto.ChancePerMillion;
            existing.MinBoxStars = dto.MinBoxStars;
            existing.Enabled = dto.Enabled;
            existing.SortOrder = dto.SortOrder;

            await _repo.EnsureSpecialTagAsync(dto.ItemBlueprintId);
            await _repo.UpdateAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (await _repo.GetByIdAsync(id) == null) throw new KeyNotFoundException($"LootboxSpecialEntry with id {id} not found.");
            // The blueprint keeps its Lootbox Special tag; remove it on the blueprint to return it to the pools.
            await _repo.DeleteAsync(id);
        }

        public async Task<PagedResultDto<LootboxSpecialEntryDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));
            var result = await _repo.SearchAsync(_mapper.Map<PagedQuery>(queryDto));
            return _mapper.Map<PagedResultDto<LootboxSpecialEntryDto>>(result);
        }

        private async Task ValidateAsync(LootboxSpecialEntryDto dto)
        {
            if (dto.ChancePerMillion < 0 || dto.ChancePerMillion > MaxChancePerMillion)
                throw new ArgumentException($"chancePerMillion must be 0-{MaxChancePerMillion}.", nameof(dto));
            if (dto.MinBoxStars < 1 || dto.MinBoxStars > LootboxRollEngine.MaxBoxStars)
                throw new ArgumentException($"minBoxStars must be 1-{LootboxRollEngine.MaxBoxStars}.", nameof(dto));
            if (!await _repo.BlueprintExistsAsync(dto.ItemBlueprintId))
                throw new ArgumentException($"ItemBlueprint with id {dto.ItemBlueprintId} not found.", nameof(dto));
            if (dto.LootboxTypeId is int typeId && !await _repo.TypeExistsAsync(typeId))
                throw new ArgumentException($"LootboxType with id {typeId} not found.", nameof(dto));
        }
    }
}
