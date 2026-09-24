using System.Threading.Tasks;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces;

/// <summary>
/// Salary payout logic (docs/specs/user-features/DESIGN.md §5,
/// docs/specs/user-features/IMPLEMENTATION_PLAN.md §6).
/// </summary>
public interface ISalaryService
{
    /// <summary>
    /// Pays out the gap since the user's last payout, if at least an hour has passed — the
    /// offline-gap fix vision §5.4 calls for (a payout covering the gap on next join, rather than
    /// lost). Intended trigger: knk-plugin calling this on player join (not built this phase; see
    /// IMPLEMENTATION_PLAN.md §6's knk-web-api-only scope). Throws KeyNotFoundException for an
    /// unknown user.
    /// </summary>
    Task<SalaryPayoutResultDto> PayOutAsync(int userId);

    /// <summary>
    /// The rank multiplier PayOutAsync would use right now, without paying anything out — the
    /// product of SalaryMultiplier across every currently-active PermissionGroup membership the
    /// user holds (1.0 if none). Backs the user-management admin module's salary state display
    /// (docs/specs/user-management/DESIGN.md §2), which needs to show the current multiplier
    /// breakdown without triggering a real payout as a side effect.
    /// </summary>
    Task<decimal> GetCurrentRankMultiplierAsync(int userId);
}
