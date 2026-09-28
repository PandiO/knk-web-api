using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    /// <summary>
    /// Warp destinations (docs/specs/teleport/DESIGN.md §3.7): domains with their Location and the
    /// title bracket / premium group they require loaded (read-only, untracked), and the teleport
    /// fee keys voided by a refund that came before the charge.
    /// </summary>
    public interface ITeleportDestinationRepository
    {
        /// <summary>Every domain (any subtype) with TeleportEnabled.</summary>
        Task<List<Domain>> GetEnabledAsync();

        /// <summary>One domain of any subtype, or null.</summary>
        Task<Domain?> GetByIdAsync(int domainId);

        /// <summary>Whether a refund voided this plugin-scope idempotency key.</summary>
        Task<bool> IsFeeKeyVoidAsync(string idempotencyKey);

        /// <summary>
        /// Void a key (saved at once, inside the caller's transaction). False when it already was
        /// void, also when a concurrent request voided it first.
        /// </summary>
        Task<bool> VoidFeeKeyAsync(TeleportFeeVoid marker);
    }
}
