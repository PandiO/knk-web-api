using System;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Statistics;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Viewer-filtered statistics reads (KNG-34, IMPLEMENTATION_PLAN.md §3.1, §4). Every method
    /// applies the visibility rules for <paramref name="viewer"/>; null = unknown or inactive user.
    /// Bad arguments throw <see cref="StatisticsValidationException"/>; hidden data
    /// <see cref="StatisticsHiddenException"/>.
    /// </summary>
    public interface IStatisticsQueryService
    {
        StatisticsCatalogDto GetCatalog();

        Task<PlayerStatisticsDto?> GetAsync(int userId, StatisticsViewer viewer, string? period, DateOnly? date,
            CancellationToken ct = default);

        Task<StatisticSeriesDto?> GetSeriesAsync(int userId, StatisticsViewer viewer, string? metric, string? context,
            string? granularity, DateOnly? from, DateOnly? to, CancellationToken ct = default);

        Task<PagedResultDto<TitleChangeDto>?> GetTitleHistoryAsync(int userId, StatisticsViewer viewer, int page, int pageSize,
            CancellationToken ct = default);

        Task<PagedResultDto<DiscoveryListItemDto>?> GetDiscoveriesAsync(int userId, StatisticsViewer viewer, int page, int pageSize,
            CancellationToken ct = default);
    }
}
