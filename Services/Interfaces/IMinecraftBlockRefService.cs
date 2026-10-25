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
        /// <summary>A picker search (KNG-200): the saved block states, each with its material's name and icon.</summary>
        Task<PagedResultDto<MinecraftBlockRefListDto>> SearchPickerAsync(PagedQueryDto queryDto);
        Task UpdateAsync(int id, MinecraftBlockRefUpdateDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<MinecraftBlockRefListDto>> SearchAsync(PagedQueryDto queryDto);
    }
}
