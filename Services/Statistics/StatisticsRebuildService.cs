using System;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services.Statistics
{
    /// <summary>Rebuilds the ledger, Siege and/or duel projections (see <see cref="IStatisticsRebuildService"/>).
    /// Plugin-reported statistics are never touched: the projectors own disjoint (metric, context) rows.</summary>
    public class StatisticsRebuildService : IStatisticsRebuildService
    {
        private const int MaxRuns = 100_000;

        private readonly LedgerStatisticsProjector _ledger;
        private readonly SiegeStatisticsProjector _siege;
        private readonly DuelStatisticsProjector? _duel;

        public StatisticsRebuildService(LedgerStatisticsProjector ledger, SiegeStatisticsProjector siege,
            DuelStatisticsProjector? duel = null)
        {
            _ledger = ledger;
            _siege = siege;
            _duel = duel;
        }

        public async Task<int> RebuildAsync(string projection, int? userId, CancellationToken ct = default)
        {
            var which = (projection ?? "").Trim().ToLowerInvariant();
            if (which is not ("ledger" or "siege" or "duel" or "all"))
            {
                throw new StatisticsValidationException("UnknownProjection", "projection must be ledger, siege, duel or all.");
            }
            var count = 0;
            if (which is "ledger" or "all")
            {
                count += await _ledger.RebuildAsync(userId, ct);
                if (userId == null) count += await DrainAsync(_ledger.ProjectNextAsync, ct);
            }
            if (which is "siege" or "all")
            {
                count += await _siege.RebuildAsync(userId, ct);
                if (userId == null) count += await DrainAsync(_siege.ProjectNextAsync, ct);
            }
            if (which is "duel" or "all" && _duel != null)
            {
                count += await _duel.RebuildAsync(userId, ct);
                if (userId == null) count += await DrainAsync(_duel.ProjectNextAsync, ct);
            }
            return count;
        }

        private static async Task<int> DrainAsync(Func<CancellationToken, Task<int>> run, CancellationToken ct)
        {
            var total = 0;
            for (var i = 0; i < MaxRuns; i++)
            {
                var count = await run(ct);
                if (count == 0) break;
                total += count;
            }
            return total;
        }
    }
}
