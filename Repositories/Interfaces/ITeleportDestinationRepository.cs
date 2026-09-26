using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    /// <summary>
    /// Warp destinations (docs/specs/teleport/DESIGN.md §3.7): domains with their Location and the
    /// title bracket / premium group they require loaded. Read-only, untracked.
    /// </summary>
    public interface ITeleportDestinationRepository
    {
        /// <summary>Every domain (any subtype) with TeleportEnabled.</summary>
        Task<List<Domain>> GetEnabledAsync();

        /// <summary>One domain of any subtype, or null.</summary>
        Task<Domain?> GetByIdAsync(int domainId);
    }
}
