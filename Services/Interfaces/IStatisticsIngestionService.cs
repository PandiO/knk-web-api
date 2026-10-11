using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>Applies plugin statistics batches (KNG-34, IMPLEMENTATION_PLAN.md §3.1).</summary>
    public interface IStatisticsIngestionService
    {
        /// <summary>
        /// Applies one batch in one transaction. A batch id seen before applies nothing
        /// (<c>duplicate: true</c>). Invalid entries are rejected one by one with a code; the others
        /// are applied. Throws <see cref="System.ArgumentException"/> for structural errors (no batch
        /// id, too many entries).
        /// </summary>
        Task<StatisticsBatchResultDto> IngestAsync(StatisticsBatchDto batch, CancellationToken ct = default);
    }
}
