using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    public interface ISiegeLobbyService
    {
        Task<IEnumerable<SiegeLobbyReadDto>> GetAllAsync();
        Task<SiegeLobbyReadDto?> GetByIdAsync(int id);
        Task<SiegeLobbyReadDto> CreateAsync(SiegeLobbyUpsertDto dto);
        Task UpdateAsync(int id, SiegeLobbyUpsertDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<SiegeLobbyListDto>> SearchAsync(PagedQueryDto query);
        Task<SiegeRuntimeConfigDto> GetRuntimeConfigAsync();
    }
}
