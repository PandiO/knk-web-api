using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    /// <summary>
    /// Data access of landmarks (PandiO/knk-workspace#983, first #708). Every read loads the pin's Location, the parent
    /// domain and the sub-regions.
    /// </summary>
    public interface ILandmarkRepository
    {
        /// <summary>Every landmark, or those whose pin is in <paramref name="world"/> (case-insensitive), by name.</summary>
        Task<IReadOnlyList<Landmark>> GetAllAsync(string? world = null);
        Task<Landmark?> GetByIdAsync(int id);
        Task AddAsync(Landmark entity);
        Task UpdateAsync(Landmark entity);

        /// <summary>Deletes the landmark (its sub-regions go with it); landmarks it was the parent of become top level.</summary>
        Task DeleteAsync(Landmark entity);

        /// <summary>
        /// The search of the dashboard and the domain pages. Filters: <c>parentDomainId</c> (or its #708 name
        /// <c>domainId</c>), <c>world</c>, <c>navigationEnabled</c> (<c>true</c>/<c>false</c>), <c>areaKind</c>; the
        /// search term matches the name, the world and the parent's name. Sort keys: <c>name</c>, <c>world</c>,
        /// <c>parentDomainName</c> (or <c>domainName</c>), otherwise the id.
        /// </summary>
        Task<PagedResult<Landmark>> SearchAsync(PagedQuery query);

        /// <summary>Another landmark (not <paramref name="exceptId"/>) in <paramref name="world"/> already has this name, ignoring case.</summary>
        Task<bool> NameTakenAsync(string name, string world, int? exceptId);

        Task<Domain?> GetDomainAsync(int id);

        /// <summary>The one-owner rule (<see cref="Services.DomainRegionOwnership"/>): who else holds the region, or null.</summary>
        Task<string?> RegionTakenByAsync(string regionId, string? world, int? exceptDomainId);

        /// <summary>LD17: a domain other than <paramref name="exceptDomainId"/> already has this Location (unique index).</summary>
        Task<bool> LocationUsedByOtherDomainAsync(int locationId, int? exceptDomainId);

        /// <summary>LD23: the ids of the domain's ancestors, nearest first, with a cycle guard.</summary>
        Task<IReadOnlyList<int>> AncestorIdsAsync(int domainId);

        /// <summary>Rule 3.4: the landmark's sub-region rows go when its area stops being its own region.</summary>
        Task RemoveSubRegionsAsync(int domainId);
    }
}
