using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services
{
    public interface IMinecraftBlockRefService
    {
        Task<IEnumerable<MinecraftBlockRefDto>> GetAllAsync();
        Task<MinecraftBlockRefDto?> GetByIdAsync(int id);
        Task<MinecraftBlockRefDto> CreateAsync(MinecraftBlockRefCreateDto dto);
        /// <summary>The saved ref for this canonical state, or a new one (KNG-200 phase 1).</summary>
        Task<MinecraftBlockRefDto> GetOrCreateAsync(MinecraftBlockRefCreateDto dto);
        Task<IEnumerable<MinecraftHybridBlockOptionDto>> GetHybridAsync(string? search = null, string? category = null, int? take = null);
        Task<PagedResultDto<MinecraftBlockRefListDto>> SearchHybridAsync(PagedQueryDto queryDto);
        Task UpdateAsync(int id, MinecraftBlockRefUpdateDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<MinecraftBlockRefListDto>> SearchAsync(PagedQueryDto queryDto);
    }
}
