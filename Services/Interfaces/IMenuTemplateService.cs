using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    public interface IMenuTemplateService
    {
        Task<IEnumerable<MenuTemplateListDto>> GetAllAsync();
        Task<MenuTemplateDto?> GetByIdAsync(int id);
        Task<MenuTemplateDto?> GetByKeyAsync(string key);
        Task<MenuTemplateDto> CreateAsync(MenuTemplateDto dto);
        Task UpdateAsync(int id, MenuTemplateDto dto);
        Task DeleteAsync(int id);
    }
}
