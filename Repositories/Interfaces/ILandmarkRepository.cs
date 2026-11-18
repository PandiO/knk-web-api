using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    /// <summary>Data access of landmarks (PandiO/knk-workspace#708). Every read loads the Location and the Domain.</summary>
    public interface ILandmarkRepository
    {
        /// <summary>Every landmark, or those whose pin is in <paramref name="world"/> (case-insensitive), by name.</summary>
        Task<IReadOnlyList<Landmark>> GetAllAsync(string? world = null);
        Task<Landmark?> GetByIdAsync(int id);
        Task AddAsync(Landmark entity);
        Task UpdateAsync(Landmark entity);
        Task DeleteAsync(Landmark entity);

        /// <summary>
        /// The search of the dashboard and the domain pages. Filters: <c>domainId</c>, <c>world</c>,
        /// <c>navigationEnabled</c> (<c>true</c>/<c>false</c>); the search term matches the name, the world and the domain's
        /// name. Sort keys: <c>name</c>, <c>world</c>, <c>domainName</c>, otherwise the id.
        /// </summary>
        Task<PagedResult<Landmark>> SearchAsync(PagedQuery query);

        /// <summary>Another landmark (not <paramref name="exceptId"/>) in <paramref name="world"/> already has this name, ignoring case.</summary>
        Task<bool> NameTakenAsync(string name, string world, int? exceptId);

        Task<Domain?> GetDomainAsync(int id);
    }
}
