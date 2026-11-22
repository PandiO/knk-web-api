using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>Landmarks, a Domain subtype (landmark-domain DESIGN, PandiO/knk-workspace#983; first off-road #708).</summary>
    public interface ILandmarkService
    {
        /// <summary>Every landmark, or the landmarks of one world (the world of their pin), by name.</summary>
        Task<IReadOnlyList<LandmarkReadDto>> GetAllAsync(string? world = null);
        Task<LandmarkReadDto?> GetByIdAsync(int id);
        Task<LandmarkReadDto> CreateAsync(LandmarkCreateDto dto);
        Task<LandmarkReadDto> UpdateAsync(int id, LandmarkUpdateDto dto);
        Task DeleteAsync(int id);

        /// <summary>
        /// LD19: <c>PUT api/Domains/{id}</c> on a landmark (the domain dashboard's edit, the caller already checked for the
        /// landmark rights): the domain fields of <paramref name="domain"/> (name, description, entry and exit, pin,
        /// region) saved through the landmark rules. A region makes the area <c>OwnRegion</c>; none keeps
        /// <c>LinkedDomain</c> and makes any other area a point.
        /// </summary>
        Task<LandmarkReadDto> UpdateFromDomainAsync(int id, Models.Domain domain);
        Task<PagedResultDto<LandmarkListDto>> SearchAsync(PagedQueryDto query);
    }
}
