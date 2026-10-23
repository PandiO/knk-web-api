using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services
{
    /// <summary>A domain's sub-regions (KNG-254, knk-workspace docs/specs/domain-subregions/DESIGN.md §4).</summary>
    public interface IDomainSubRegionService
    {
        Task<IReadOnlyList<DomainSubRegionDto>> GetByDomainAsync(int domainId);
        Task<DomainSubRegionDto?> GetByIdAsync(int id);

        /// <summary>Saves the sub-region and renames its region to its final name. Throws ArgumentException (also
        /// <see cref="DomainWorldException"/>: wrong world, region taken) and KeyNotFoundException (no such domain).</summary>
        Task<DomainSubRegionDto> CreateAsync(DomainSubRegionDto dto);

        /// <summary>As <see cref="CreateAsync"/>; a sub-region can't move to another domain.</summary>
        Task<DomainSubRegionDto> UpdateAsync(int id, DomainSubRegionDto dto);

        /// <summary>Removes the row; its WorldGuard region becomes an orphan the game server deletes after the grace period.</summary>
        Task DeleteAsync(int id);
    }
}
