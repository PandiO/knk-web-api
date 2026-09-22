using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    public interface ITagService
    {
        Task<IEnumerable<TagReadDto>> GetAllAsync();
        Task<TagReadDto?> GetByIdAsync(int id);
        Task<TagReadDto> CreateAsync(TagCreateDto dto);
        Task UpdateAsync(int id, TagUpdateDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<TagListDto>> SearchAsync(PagedQueryDto query);
    }
}
