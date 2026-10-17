using System.Collections.Generic;
using System;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces;

public interface ILocationRepository
{
    Task<IEnumerable<Location>> GetAllAsync();
    Task<Location?> GetByIdAsync(int id);
    Task AddLocationAsync(Location location);
    Task UpdateLocationAsync(Location location);
    Task DeleteLocationAsync(int id);
    /// <summary>The domains whose own Location this is, as "Town 'Kardenna'" (KNG-118 D3).</summary>
    Task<IReadOnlyList<string>> GetDomainsUsingAsync(int locationId);
    Task<PagedResult<Location>> SearchAsync(PagedQuery query);
}
