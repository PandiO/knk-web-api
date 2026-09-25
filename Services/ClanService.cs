using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    // Siege Phase 1 (docs/specs/siege-minigame/DESIGN.md §3.2). Same error convention as
    // BannerDesignService: ArgumentException -> 400, KeyNotFoundException -> 404,
    // InvalidOperationException -> 409 (a town that already has a default clan).
    public class ClanService : IClanService
    {
        // Bukkit ChatColor colour names (formatting codes like BOLD aren't colours).
        public static readonly IReadOnlySet<string> ChatColorNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "BLACK", "DARK_BLUE", "DARK_GREEN", "DARK_AQUA", "DARK_RED", "DARK_PURPLE", "GOLD", "GRAY",
            "DARK_GRAY", "BLUE", "GREEN", "AQUA", "RED", "LIGHT_PURPLE", "YELLOW", "WHITE"
        };

        private readonly IClanRepository _repo;
        private readonly IMapper _mapper;

        public ClanService(IClanRepository repo, IMapper mapper)
        {
            _repo = repo;
            _mapper = mapper;
        }

        public async Task<IEnumerable<ClanReadDto>> GetAllAsync()
        {
            return _mapper.Map<IEnumerable<ClanReadDto>>(await _repo.GetAllAsync());
        }

        public async Task<ClanReadDto?> GetByIdAsync(int id)
        {
            if (id <= 0) return null;
            var entity = await _repo.GetByIdAsync(id);
            return entity == null ? null : _mapper.Map<ClanReadDto>(entity);
        }

        public async Task<ClanReadDto?> GetDefaultForTownAsync(int townId)
        {
            if (townId <= 0) return null;
            var entity = await _repo.GetDefaultForTownAsync(townId);
            return entity == null ? null : _mapper.Map<ClanReadDto>(entity);
        }

        public async Task<ClanReadDto> CreateAsync(ClanUpsertDto dto)
        {
            var entity = new Clan();
            await ApplyAsync(entity, dto, existingId: null);
            await _repo.AddAsync(entity);

            // Re-read so the response carries the banner graph and town name.
            var created = await _repo.GetByIdAsync(entity.Id);
            return _mapper.Map<ClanReadDto>(created ?? entity);
        }

        public async Task UpdateAsync(int id, ClanUpsertDto dto)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            var existing = await _repo.GetByIdAsync(id)
                ?? throw new KeyNotFoundException($"Clan with id {id} not found.");
            await ApplyAsync(existing, dto, existingId: id);
            await _repo.UpdateAsync(existing);
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));
            _ = await _repo.GetByIdAsync(id)
                ?? throw new KeyNotFoundException($"Clan with id {id} not found.");
            // Siege teams reference their clan with Restrict (Siege Phase 2).
            if (await _repo.IsUsedBySiegeTeamAsync(id))
                throw new InvalidOperationException("This clan is used by a siege team. Change that team's clan before deleting it.");
            await _repo.DeleteAsync(id);
        }

        public async Task<PagedResultDto<ClanListDto>> SearchAsync(PagedQueryDto queryDto)
        {
            if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));
            var result = await _repo.SearchAsync(_mapper.Map<PagedQuery>(queryDto));
            return _mapper.Map<PagedResultDto<ClanListDto>>(result);
        }

        private async Task ApplyAsync(Clan target, ClanUpsertDto dto, int? existingId)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            var name = dto.Name?.Trim();
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Name is required.");
            if (name.Length > 100) throw new ArgumentException("Name can be at most 100 characters.");

            var chatColor = string.IsNullOrWhiteSpace(dto.ChatColor) ? "WHITE" : dto.ChatColor.Trim().ToUpperInvariant();
            if (!ChatColorNames.Contains(chatColor))
                throw new ArgumentException($"Unknown chat colour '{dto.ChatColor}'. Use a Bukkit colour name such as GOLD or DARK_RED.");

            if (dto.BannerDesignId <= 0) throw new ArgumentException("A banner is required.");
            if (!await _repo.BannerDesignExistsAsync(dto.BannerDesignId))
                throw new ArgumentException($"BannerDesign with id {dto.BannerDesignId} not found.");

            // The wizard can send 0 for an empty optional picker - treat it as "no town".
            int? townId = dto.DefaultForTownId is > 0 ? dto.DefaultForTownId : null;
            if (townId.HasValue)
            {
                if (!await _repo.TownExistsAsync(townId.Value))
                    throw new ArgumentException($"Town with id {townId} not found.");

                var current = await _repo.GetDefaultForTownAsync(townId.Value);
                if (current != null && current.Id != existingId)
                    throw new InvalidOperationException(
                        $"'{current.Name}' is already the default clan for this town. A town can have only one default clan.");
            }

            target.Name = name;
            target.IsNpc = dto.IsNpc;
            target.ChatColor = chatColor;
            target.BannerDesignId = dto.BannerDesignId;
            target.DefaultForTownId = townId;
        }
    }
}
