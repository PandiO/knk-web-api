using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories
{
    public interface IDomainRepository
    {
        Task<IEnumerable<Domain>> GetAllAsync();
        Task<Domain?> GetByIdAsync(int id);
        Task<Domain?> GetByWgRegionNameAsync(string regionName);
        /// <summary>
        /// KNG-111: the domain whose region is <paramref name="regionName"/> in <paramref name="worldName"/>. A domain not
        /// yet assigned a world still matches as a fallback; a domain of another world never does. A null world is the
        /// world-blind lookup.
        /// </summary>
        Task<Domain?> GetByWgRegionNameAsync(string regionName, string? worldName);
        Task AddDomainAsync(Domain domain);
        Task UpdateDomainAsync(Domain domain);
        Task DeleteDomainAsync(int id);
        Task<PagedResult<Domain>> SearchAsync(PagedQuery query);
        /// <summary>The /navigate default of each domain type that has a row (KNG-73), by type name.</summary>
        Task<IReadOnlyDictionary<string, NavigationDestinationMode>> GetNavigationDefaultsAsync();
        /// <summary>The road access of each domain type that has a row (rev. 7 Part C, KNG-92), by type name.</summary>
        Task<IReadOnlyDictionary<string, RoadAccessRule>> GetRoadAccessDefaultsAsync();
    }
}
