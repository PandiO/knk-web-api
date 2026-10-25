using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces;

public interface IMinecraftEnchantmentRefService
{
    Task<IEnumerable<MinecraftEnchantmentRefDto>> GetAllAsync();
    Task<MinecraftEnchantmentRefDto?> GetByIdAsync(int id);
    Task<MinecraftEnchantmentRefDto> CreateAsync(MinecraftEnchantmentRefCreateDto dto);
    Task<MinecraftEnchantmentRefDto> GetOrCreateAsync(string namespaceKey, string? category = null, string? legacyName = null);
    Task UpdateAsync(int id, MinecraftEnchantmentRefUpdateDto dto);
    Task DeleteAsync(int id);
    Task<PagedResultDto<MinecraftEnchantmentRefListDto>> SearchAsync(PagedQueryDto query);
    Task<PagedResultDto<MinecraftEnchantmentRefListDto>> SearchPickerAsync(PagedQueryDto query);
}
