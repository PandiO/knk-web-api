using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>Landmarks (off-road navigation phase 5.1, PandiO/knk-workspace#708, DESIGN § 6).</summary>
    public interface ILandmarkService
    {
        /// <summary>Every landmark, or the landmarks of one world (the world of their pin), by name.</summary>
        Task<IReadOnlyList<LandmarkReadDto>> GetAllAsync(string? world = null);
        Task<LandmarkReadDto?> GetByIdAsync(int id);
        Task<LandmarkReadDto> CreateAsync(LandmarkCreateDto dto);
        Task<LandmarkReadDto> UpdateAsync(int id, LandmarkUpdateDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<LandmarkListDto>> SearchAsync(PagedQueryDto query);
    }
}
