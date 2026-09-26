using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    public interface IClanService
    {
        Task<IEnumerable<ClanReadDto>> GetAllAsync();
        Task<ClanReadDto?> GetByIdAsync(int id);
        Task<ClanReadDto?> GetDefaultForTownAsync(int townId);
        Task<ClanReadDto> CreateAsync(ClanUpsertDto dto);
        Task UpdateAsync(int id, ClanUpsertDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<ClanListDto>> SearchAsync(PagedQueryDto query);
    }
}
