using knkwebapi_v2.Attributes;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Minimal lookups for pickers and relation labels across areas (PandiO/knk-workspace#280 AM2, knk-workspace
    /// docs/specs/dashboard-access-model/DESIGN.md § 4.6.3): id, label and one detail line through the registry's
    /// search, get and label delegates (<see cref="DashboardAccess"/>, <see cref="DashboardLookup"/>), so a world builder
    /// can pick a Clan without reading clans in full.
    /// </summary>
    public interface ILookupService
    {
        /// <summary>One page of the type's own search, as labels.</summary>
        Task<LookupSearchResponseDto> SearchAsync(DashboardTypeAccess type, LookupSearchRequestDto request);

        /// <summary>One record as a label, or null when it doesn't exist.</summary>
        Task<LookupItemDto?> GetAsync(DashboardTypeAccess type, int id);
    }

    public class LookupService : ILookupService
    {
        public const int MaxPageSize = 50;

        private readonly IServiceProvider _services;

        public LookupService(IServiceProvider services)
        {
            _services = services;
        }

        public async Task<LookupSearchResponseDto> SearchAsync(DashboardTypeAccess type, LookupSearchRequestDto request)
        {
            if (!type.LookupSearchable) throw new InvalidOperationException($"{type.TypeName} has no lookup search.");

            var term = string.IsNullOrWhiteSpace(request.SearchTerm) ? null : request.SearchTerm.Trim();
            var query = new PagedQueryDto
            {
                PageNumber = Math.Max(1, request.Page),
                PageSize = Math.Clamp(request.PageSize, 1, MaxPageSize),
                SearchTerm = term,
            };
            if (type.Lookup!.Query != null) query = type.Lookup.Query(term, query);

            var page = await type.Search!(_services, query);
            return new LookupSearchResponseDto
            {
                Items = page.Items.Select(row => Item(type, row)).OfType<LookupItemDto>().ToList(),
                Total = page.Total,
            };
        }

        public async Task<LookupItemDto?> GetAsync(DashboardTypeAccess type, int id)
        {
            if (!type.LookupGettable) throw new InvalidOperationException($"{type.TypeName} has no lookup by id.");
            if (id <= 0) return null;

            try
            {
                var row = await type.Lookup!.Get!(_services, id);
                return row == null ? null : Item(type, row, id);
            }
            catch (KeyNotFoundException)
            {
                return null;
            }
        }

        private static LookupItemDto? Item(DashboardTypeAccess type, object row, int? knownId = null)
        {
            var id = DashboardLookups.Id(row) ?? knownId;
            if (id is not > 0) return null;
            var lookup = type.Lookup!;
            return new LookupItemDto
            {
                Id = id.Value,
                Label = lookup.Label(row) ?? $"{type.TypeName} #{id}",
                Detail = lookup.Detail?.Invoke(row),
            };
        }
    }
}
