using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>The per-type <c>/navigate</c> defaults (KNG-73) for the web-app road admin page.</summary>
    public interface IDomainNavigationSettingsService
    {
        /// <summary>Town, District, Structure and GateStructure, each with its default and its override count.</summary>
        Task<List<DomainNavigationDefaultDto>> GetTypeDefaultsAsync();

        /// <summary>Sets a type's default. Unknown type → KeyNotFoundException; bad mode → ArgumentException.</summary>
        Task<DomainNavigationDefaultDto> UpdateTypeDefaultAsync(string domainType, UpdateDomainNavigationDefaultDto dto);
    }
}
