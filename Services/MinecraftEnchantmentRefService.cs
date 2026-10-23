using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.MinecraftCatalog;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services;

public class MinecraftEnchantmentRefService : IMinecraftEnchantmentRefService
{
    private readonly IMinecraftEnchantmentRefRepository _repo;
    private readonly IMapper _mapper;
    private readonly ILogger<MinecraftEnchantmentRefService> _logger;

    public MinecraftEnchantmentRefService(
        IMinecraftEnchantmentRefRepository repo,
        IMapper mapper,
        ILogger<MinecraftEnchantmentRefService> logger)
    {
        _repo = repo;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<IEnumerable<MinecraftEnchantmentRefDto>> GetAllAsync()
    {
        var items = await _repo.GetAllAsync();
        return _mapper.Map<IEnumerable<MinecraftEnchantmentRefDto>>(items);
    }

    public async Task<MinecraftEnchantmentRefDto?> GetByIdAsync(int id)
    {
        if (id <= 0) return null;
        var entity = await _repo.GetByIdAsync(id);
        return _mapper.Map<MinecraftEnchantmentRefDto>(entity);
    }

    public async Task<MinecraftEnchantmentRefDto> CreateAsync(MinecraftEnchantmentRefCreateDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.NamespaceKey))
            throw new ArgumentException("NamespaceKey is required.", nameof(dto));

        var entity = _mapper.Map<MinecraftEnchantmentRef>(dto);
        entity.NamespaceKey = MinecraftEnchantmentKey.Normalize(dto.NamespaceKey, isCustom: false);
        entity.IsCustom = MinecraftEnchantmentKey.IsCustomKey(entity.NamespaceKey);
        
        await _repo.AddAsync(entity);
        return _mapper.Map<MinecraftEnchantmentRefDto>(entity);
    }

    public async Task<MinecraftEnchantmentRefDto> GetOrCreateAsync(string namespaceKey, string? category = null, string? legacyName = null)
    {
        // KNG-250: no catalogue lookup; an unknown key becomes an Unverified ref the next scan confirms or cleans up.
        var key = MinecraftEnchantmentKey.Normalize(namespaceKey, isCustom: false);
        var existing = await _repo.GetByNamespaceKeyAsync(key);
        if (existing != null)
        {
            return _mapper.Map<MinecraftEnchantmentRefDto>(existing);
        }

        var entity = new MinecraftEnchantmentRef
        {
            NamespaceKey = key,
            LegacyName = legacyName,
            Category = category,
            DisplayName = MinecraftNamespaceKey.DisplayNameFromKey(key),
            IsCustom = MinecraftEnchantmentKey.IsCustomKey(key),
        };

        try
        {
            await _repo.AddAsync(entity);
            _logger.LogInformation("Created unverified enchantment {NamespaceKey} with Id {Id}", key, entity.Id);
            return _mapper.Map<MinecraftEnchantmentRefDto>(entity);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase) == true ||
                                           ex.InnerException?.Message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Race condition: another request created it; fetch and return
            _logger.LogInformation("Enchantment {NamespaceKey} created by another request; fetching...", key);
            var raceWinner = await _repo.GetByNamespaceKeyAsync(key);
            if (raceWinner != null)
                return _mapper.Map<MinecraftEnchantmentRefDto>(raceWinner);
            throw;
        }
    }

    public async Task UpdateAsync(int id, MinecraftEnchantmentRefUpdateDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));

        var existing = await _repo.GetByIdAsync(id);
        if (existing == null)
            throw new KeyNotFoundException($"MinecraftEnchantmentRef with id {id} not found.");

        _mapper.Map(dto, existing);
        await _repo.UpdateAsync(existing);
    }

    public async Task DeleteAsync(int id)
    {
        if (id <= 0) throw new ArgumentException("Invalid id.", nameof(id));

        var existing = await _repo.GetByIdAsync(id);
        if (existing == null)
            throw new KeyNotFoundException($"MinecraftEnchantmentRef with id {id} not found.");

        await _repo.DeleteAsync(existing);
    }

    public async Task<PagedResultDto<MinecraftEnchantmentRefListDto>> SearchAsync(PagedQueryDto queryDto)
    {
        if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));
        var query = _mapper.Map<PagedQuery>(queryDto);
        var result = await _repo.SearchAsync(query);
        return _mapper.Map<PagedResultDto<MinecraftEnchantmentRefListDto>>(result);
    }

    public async Task<PagedResultDto<MinecraftEnchantmentRefListDto>> SearchPickerAsync(PagedQueryDto queryDto)
    {
        if (queryDto == null) throw new ArgumentNullException(nameof(queryDto));

        // KNG-250: the database is the whole catalogue (the server scan fills it, vanilla and custom) - origin and
        // category filters, Missing rows hidden, sorted by display name by default.
        var query = _mapper.Map<PagedQuery>(queryDto);
        query.Filters = MinecraftPickerFilters.ForEnchantmentDatabase(query.Filters);
        if (string.IsNullOrWhiteSpace(query.SortBy)) query.SortBy = "displayName";
        var result = await _repo.SearchAsync(query);
        return _mapper.Map<PagedResultDto<MinecraftEnchantmentRefListDto>>(result);
    }
}
