using System.Text.RegularExpressions;
using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Lootbox;

namespace knkwebapi_v2.Services
{
    public class LootboxSpawnAreaService : ILootboxSpawnAreaService
    {
        // The in-game command addresses areas by name and derives the region id lootbox_<slug> from it (DESIGN.md §3.4).
        public static readonly Regex NamePattern = new("^[A-Za-z0-9_-]{1,32}$", RegexOptions.Compiled);
        public const int MinSpawnIntervalSeconds = 60;

        private readonly ILootboxSpawnAreaRepository _repo;
        private readonly IMapper _mapper;

        public LootboxSpawnAreaService(ILootboxSpawnAreaRepository repo, IMapper mapper)
        {
            _repo = repo;
            _mapper = mapper;
        }

        public async Task<IEnumerable<LootboxSpawnAreaDto>> GetAllAsync()
        {
            return _mapper.Map<IEnumerable<LootboxSpawnAreaDto>>(await _repo.GetAllAsync());
        }

        public async Task<LootboxSpawnAreaDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var area = await _repo.GetByIdAsync(id);
            return area == null ? null : _mapper.Map<LootboxSpawnAreaDto>(area);
        }

        public async Task<LootboxSpawnAreaDto> CreateAsync(LootboxSpawnAreaDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            Normalize(dto);
            await ValidateAsync(dto, null);

            var entity = _mapper.Map<LootboxSpawnArea>(dto);
            foreach (var typeId in dto.AllowedTypes.Select(t => t.LootboxTypeId).Distinct())
            {
                entity.AllowedTypes.Add(new LootboxSpawnAreaType { LootboxTypeId = typeId });
            }

            await _repo.AddAsync(entity);
            return _mapper.Map<LootboxSpawnAreaDto>(await _repo.GetByIdAsync(entity.Id) ?? entity);
        }

        public async Task UpdateAsync(int id, LootboxSpawnAreaDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));

            var existing = await _repo.GetByIdAsync(id) ?? throw new KeyNotFoundException($"LootboxSpawnArea with id {id} not found.");
            Normalize(dto);
            await ValidateAsync(dto, id);

            existing.Name = dto.Name;
            existing.World = dto.World;
            existing.WgRegionId = dto.WgRegionId;
            existing.Enabled = dto.Enabled;
            existing.MaxActive = dto.MaxActive;
            existing.SpawnIntervalSeconds = dto.SpawnIntervalSeconds;
            existing.SpawnChancePercent = dto.SpawnChancePercent;
            existing.MinOnlinePlayers = dto.MinOnlinePlayers;
            existing.MinDistanceFromPlayers = dto.MinDistanceFromPlayers;
            existing.LifetimeMinutes = dto.LifetimeMinutes;
            existing.ExcludedRegionIds = dto.ExcludedRegionIds;

            existing.AllowedTypes.Clear();
            foreach (var typeId in dto.AllowedTypes.Select(t => t.LootboxTypeId).Distinct())
            {
                existing.AllowedTypes.Add(new LootboxSpawnAreaType { LootboxSpawnAreaId = existing.Id, LootboxTypeId = typeId });
            }

            await _repo.UpdateAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            if (await _repo.GetByIdAsync(id) == null) throw new KeyNotFoundException($"LootboxSpawnArea with id {id} not found.");
            // A delete from the web app can't reach WorldGuard, so a lootbox_ region the in-game command made stays
            // behind (DESIGN.md §3.4).
            await _repo.DeleteAsync(id);
        }

        public async Task<PagedResultDto<LootboxSpawnAreaDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));
            var result = await _repo.SearchAsync(_mapper.Map<PagedQuery>(queryDto));
            return _mapper.Map<PagedResultDto<LootboxSpawnAreaDto>>(result);
        }

        private static void Normalize(LootboxSpawnAreaDto dto)
        {
            dto.Name = dto.Name?.Trim() ?? string.Empty;
            dto.World = dto.World?.Trim() ?? string.Empty;
            dto.WgRegionId = dto.WgRegionId?.Trim() ?? string.Empty;
            var excluded = (dto.ExcludedRegionIds ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            var joined = string.Join(",", excluded);
            dto.ExcludedRegionIds = joined.Length == 0 ? null : joined;
            dto.AllowedTypes ??= new List<LootboxSpawnAreaTypeDto>();
        }

        private async Task ValidateAsync(LootboxSpawnAreaDto dto, int? id)
        {
            if (!NamePattern.IsMatch(dto.Name))
                throw new ArgumentException("Name must be 1-32 characters of A-Z, a-z, 0-9, _ or -.", nameof(dto));
            if (dto.World.Length == 0 || dto.World.Length > 64)
                throw new ArgumentException("World is required (at most 64 characters).", nameof(dto));
            if (dto.WgRegionId.Length == 0 || dto.WgRegionId.Length > 128)
                throw new ArgumentException("wgRegionId is required (at most 128 characters).", nameof(dto));
            if (dto.ExcludedRegionIds is { Length: > 1024 })
                throw new ArgumentException("excludedRegionIds can be at most 1024 characters.", nameof(dto));
            if (dto.MaxActive < 0) throw new ArgumentException("maxActive can't be negative.", nameof(dto));
            if (dto.SpawnIntervalSeconds < MinSpawnIntervalSeconds)
                throw new ArgumentException($"spawnIntervalSeconds must be at least {MinSpawnIntervalSeconds}.", nameof(dto));
            if (dto.SpawnChancePercent < 0m || dto.SpawnChancePercent > 100m)
                throw new ArgumentException("spawnChancePercent must be 0-100.", nameof(dto));
            if (dto.MinOnlinePlayers < 0) throw new ArgumentException("minOnlinePlayers can't be negative.", nameof(dto));
            if (dto.MinDistanceFromPlayers < 0) throw new ArgumentException("minDistanceFromPlayers can't be negative.", nameof(dto));
            if (dto.LifetimeMinutes < 1) throw new ArgumentException("lifetimeMinutes must be at least 1.", nameof(dto));

            var typeIds = dto.AllowedTypes.Select(t => t.LootboxTypeId).Distinct().ToList();
            var existingTypes = await _repo.GetExistingTypeIdsAsync(typeIds);
            var unknown = typeIds.Where(t => !existingTypes.Contains(t)).ToList();
            if (unknown.Count > 0)
                throw new ArgumentException($"LootboxType(s) not found: {string.Join(", ", unknown)}.", nameof(dto));

            if (await _repo.NameTakenAsync(dto.Name, id))
                throw new LootboxConflictException("NameTaken", $"A lootbox spawn area named '{dto.Name}' already exists.");
        }
    }
}
