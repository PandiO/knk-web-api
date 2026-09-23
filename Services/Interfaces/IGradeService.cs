using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    public interface IGradeService
    {
        Task<IEnumerable<GradeReadDto>> GetAllAsync();
        Task<GradeReadDto?> GetByIdAsync(int id);
        Task<GradeReadDto> CreateAsync(GradeCreateDto dto);
        Task UpdateAsync(int id, GradeUpdateDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<GradeListDto>> SearchAsync(PagedQueryDto query);
    }
}
