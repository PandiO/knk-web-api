using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    // Siege Phase 1 (docs/specs/siege-minigame/DESIGN.md §3.1). One service path per operation;
    // every write re-validates server-side (Kits precedent). Error convention matches the other
    // controllers: ArgumentException -> 400, KeyNotFoundException -> 404,
    // InvalidOperationException -> 409.
    public class BannerDesignService : IBannerDesignService
    {
        // Client render limit (vanilla renders at most 16 layers).
        public const int MaxLayers = 16;

        private readonly IBannerDesignRepository _repo;
        private readonly IMapper _mapper;

        public BannerDesignService(IBannerDesignRepository repo, IMapper mapper)
        {
            _repo = repo;
            _mapper = mapper;
        }

        public async Task<IEnumerable<BannerDesignReadDto>> GetAllAsync()
        {
            return _mapper.Map<IEnumerable<BannerDesignReadDto>>(await _repo.GetAllAsync());
        }

        public async Task<BannerDesignReadDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var entity = await _repo.GetByIdAsync(id);
            return entity == null ? null : _mapper.Map<BannerDesignReadDto>(entity);
        }

        public async Task<BannerDesignReadDto> CreateAsync(BannerDesignUpsertDto dto)
        {
            var (name, baseColor) = ValidateDesign(dto);
            var entity = new BannerDesign { Name = name, BaseColor = baseColor };
            await _repo.AddAsync(entity);
            return _mapper.Map<BannerDesignReadDto>(entity);
        }

        public async Task UpdateAsync(int id, BannerDesignUpsertDto dto)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var (name, baseColor) = ValidateDesign(dto);
            var existing = await _repo.GetByIdAsync(id)
                ?? throw new KeyNotFoundException($"BannerDesign with id {id} not found.");

            existing.Name = name;
            existing.BaseColor = baseColor;
            await _repo.UpdateAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            _ = await _repo.GetByIdAsync(id)
                ?? throw new KeyNotFoundException($"BannerDesign with id {id} not found.");
            if (await _repo.IsReferencedByClanAsync(id))
                throw new InvalidOperationException("This banner is used by a clan. Pick another banner for that clan before deleting it.");

            await _repo.DeleteAsync(id);
        }

        public async Task<PagedResultDto<BannerDesignListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));
            var result = await _repo.SearchAsync(_mapper.Map<PagedQuery>(queryDto));
            return _mapper.Map<PagedResultDto<BannerDesignListDto>>(result);
        }

        public IReadOnlyList<string> GetPatternKeys() => BannerPatternKeys.All;

        // ---- Layers ----

        public async Task<List<BannerLayerDto>> GetLayersAsync(int bannerDesignId)
        {
            _ = await _repo.GetByIdAsync(bannerDesignId)
                ?? throw new KeyNotFoundException($"BannerDesign with id {bannerDesignId} not found.");
            return _mapper.Map<List<BannerLayerDto>>(await _repo.GetLayersAsync(bannerDesignId));
        }

        public async Task<BannerLayerDto?> GetLayerByIdAsync(int id)
        {
            if (id <= 0) return null;
            var layer = await _repo.GetLayerByIdAsync(id);
            return layer == null ? null : _mapper.Map<BannerLayerDto>(layer);
        }

        public async Task<BannerLayerDto> CreateLayerAsync(int bannerDesignId, BannerLayerUpsertDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (dto.BannerDesignId.HasValue && dto.BannerDesignId.Value > 0 && dto.BannerDesignId.Value != bannerDesignId)
                throw new ArgumentException("bannerDesignId in the body doesn't match the banner in the URL.");

            var design = await _repo.GetByIdAsync(bannerDesignId)
                ?? throw new KeyNotFoundException($"BannerDesign with id {bannerDesignId} not found.");
            if (design.Layers.Count >= MaxLayers)
                throw new ArgumentException($"A banner can have at most {MaxLayers} layers.");

            var (patternKey, color) = ValidateLayer(dto);
            var sortOrder = dto.SortOrder
                ?? (design.Layers.Count == 0 ? 0 : design.Layers.Max(l => l.SortOrder) + 1);
            if (sortOrder < 0) throw new ArgumentException("sortOrder can't be negative.");

            var layer = new BannerLayer
            {
                BannerDesignId = bannerDesignId,
                SortOrder = sortOrder,
                PatternKey = patternKey,
                Color = color
            };
            await _repo.AddLayerAsync(layer);
            return _mapper.Map<BannerLayerDto>(layer);
        }

        public async Task UpdateLayerAsync(int id, BannerLayerUpsertDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));

            var layer = await _repo.GetLayerByIdAsync(id)
                ?? throw new KeyNotFoundException($"BannerLayer with id {id} not found.");
            if (dto.BannerDesignId.HasValue && dto.BannerDesignId.Value > 0 && dto.BannerDesignId.Value != layer.BannerDesignId)
                throw new ArgumentException("A layer can't be moved to another banner; delete it and add it there instead.");

            var (patternKey, color) = ValidateLayer(dto);
            if (dto.SortOrder.HasValue)
            {
                if (dto.SortOrder.Value < 0) throw new ArgumentException("sortOrder can't be negative.");
                layer.SortOrder = dto.SortOrder.Value;
            }
            layer.PatternKey = patternKey;
            layer.Color = color;
            await _repo.UpdateLayerAsync(layer);
        }

        public async Task DeleteLayerAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var layer = await _repo.GetLayerByIdAsync(id)
                ?? throw new KeyNotFoundException($"BannerLayer with id {id} not found.");
            await _repo.DeleteLayerAsync(layer);
        }

        // ---- Validation ----

        private static (string name, BannerDyeColor baseColor) ValidateDesign(BannerDesignUpsertDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var name = dto.Name?.Trim();
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Name is required.");
            if (name.Length > 100) throw new ArgumentException("Name can be at most 100 characters.");
            if (!Enum.IsDefined(dto.BaseColor)) throw new ArgumentException($"Unknown base colour '{dto.BaseColor}'.");
            return (name, dto.BaseColor);
        }

        private static (string patternKey, BannerDyeColor color) ValidateLayer(BannerLayerUpsertDto dto)
        {
            var patternKey = BannerPatternKeys.Normalize(dto.PatternKey)
                ?? throw new ArgumentException(
                    $"Unknown banner pattern '{dto.PatternKey}'. Use a key like 'minecraft:stripe_top' " +
                    "(GET /api/BannerDesigns/pattern-keys lists them all).");
            if (!Enum.IsDefined(dto.Color)) throw new ArgumentException($"Unknown layer colour '{dto.Color}'.");
            return (patternKey, dto.Color);
        }
    }
}
