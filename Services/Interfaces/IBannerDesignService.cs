using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    public interface IBannerDesignService
    {
        Task<IEnumerable<BannerDesignReadDto>> GetAllAsync();
        Task<BannerDesignReadDto?> GetByIdAsync(int id);
        Task<BannerDesignReadDto> CreateAsync(BannerDesignUpsertDto dto);
        Task UpdateAsync(int id, BannerDesignUpsertDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<BannerDesignListDto>> SearchAsync(PagedQueryDto query);
        IReadOnlyList<string> GetPatternKeys();

        Task<List<BannerLayerDto>> GetLayersAsync(int bannerDesignId);
        Task<BannerLayerDto?> GetLayerByIdAsync(int id);
        Task<BannerLayerDto> CreateLayerAsync(int bannerDesignId, BannerLayerUpsertDto dto);
        Task UpdateLayerAsync(int id, BannerLayerUpsertDto dto);
        Task DeleteLayerAsync(int id);
    }
}
