using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    public interface IBannerDesignRepository
    {
        Task<IEnumerable<BannerDesign>> GetAllAsync();
        Task<BannerDesign?> GetByIdAsync(int id);
        Task AddAsync(BannerDesign entity);
        Task UpdateAsync(BannerDesign entity);
        Task DeleteAsync(int id);
        Task<PagedResult<BannerDesign>> SearchAsync(PagedQuery query);
        Task<bool> IsReferencedByClanAsync(int bannerDesignId);
        Task<bool> IsReferencedBySiegeTeamAsync(int bannerDesignId);

        // Owned layers
        Task<BannerLayer?> GetLayerByIdAsync(int id);
        Task<List<BannerLayer>> GetLayersAsync(int bannerDesignId);
        Task AddLayerAsync(BannerLayer layer);
        Task UpdateLayerAsync(BannerLayer layer);
        Task DeleteLayerAsync(BannerLayer layer);
    }
}
