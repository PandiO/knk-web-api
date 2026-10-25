using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services
{
    public interface IMinecraftMaterialRefService
    {
        Task<IEnumerable<MinecraftMaterialRefDto>> GetAllAsync();
        Task<MinecraftMaterialRefDto?> GetByIdAsync(int id);
        Task<MinecraftMaterialRefDto> CreateAsync(MinecraftMaterialRefCreateDto dto);
        Task<MinecraftMaterialRefDto> GetOrCreateAsync(string namespaceKey, string category, string? legacyName = null);
        /// <summary>A picker search (KNG-200): kind filter, Missing rows hidden, sorted by display name.</summary>
        Task<PagedResultDto<MinecraftMaterialRefListDto>> SearchPickerAsync(PagedQueryDto queryDto);
        Task UpdateAsync(int id, MinecraftMaterialRefUpdateDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<MinecraftMaterialRefListDto>> SearchAsync(PagedQueryDto queryDto);
    }
}
