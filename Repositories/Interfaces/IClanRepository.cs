using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    public interface IClanRepository
    {
        Task<IEnumerable<Clan>> GetAllAsync();
        Task<Clan?> GetByIdAsync(int id);
        Task<Clan?> GetDefaultForTownAsync(int townId);
        Task AddAsync(Clan entity);
        Task UpdateAsync(Clan entity);
        Task DeleteAsync(int id);
        Task<PagedResult<Clan>> SearchAsync(PagedQuery query);
        Task<bool> TownExistsAsync(int townId);
        Task<bool> BannerDesignExistsAsync(int bannerDesignId);
    }
}
