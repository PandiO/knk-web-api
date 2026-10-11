using System.Threading;
using System.Threading.Tasks;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Owner-triggered rebuild of the projected statistics (KNG-34, IMPLEMENTATION_PLAN.md §4) —
    /// exposed by link 6 as POST api/statistics/rebuild (owner knk.owner.telemetry.manage).
    /// </summary>
    public interface IStatisticsRebuildService
    {
        /// <summary>
        /// Deletes and recomputes the rows of <paramref name="projection"/> ("ledger", "siege" or
        /// "all") for one user, or for everyone when <paramref name="userId"/> is null (then
        /// re-projects until caught up). Returns the source records re-projected.
        /// </summary>
        Task<int> RebuildAsync(string projection, int? userId, CancellationToken ct = default);
    }
}
