using System.Globalization;
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
        /// <summary>
        /// One page of the type's own search, as labels, narrowed by the filters the type's lookup takes (#288). Throws
        /// <see cref="LookupFilterException"/> for another filter or an unusable value.
        /// </summary>
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
            var filters = CheckFilters(type, request.Filters);
            if (filters.Count > 0)
            {
                query.Filters ??= new Dictionary<string, string>();
                foreach (var (key, value) in filters) query.Filters[key] = value;
            }

            var page = await type.Search!(_services, query);
            return new LookupSearchResponseDto
            {
                Items = page.Items.Select(row => Item(type, row)).OfType<LookupItemDto>().ToList(),
                Total = page.Total,
            };
        }

        /// <summary>
        /// The request's filters as the type's search reads them (PandiO/knk-workspace#288): the type's spelling of each
        /// key, values checked. Throws <see cref="LookupFilterException"/> for a key the lookup doesn't take or a value
        /// the search would skip (and so list more than asked); a <c>prefer…</c> key is left out instead, since it only
        /// orders the results. Blank values are refused too: the search would skip them.
        /// </summary>
        public static IReadOnlyDictionary<string, string> CheckFilters(DashboardTypeAccess type, IReadOnlyDictionary<string, string?>? requested)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (requested == null || requested.Count == 0) return result;

            var lookup = type.Lookup!;
            var notAllowed = new List<string>();
            var invalid = new List<string>();
            foreach (var (rawKey, rawValue) in requested)
            {
                var key = rawKey?.Trim() ?? "";
                var filter = key.Length == 0 ? null : lookup.FilterFor(key);
                var value = rawValue?.Trim() ?? "";
                if (filter == null)
                {
                    if (!IsPrefer(key)) notAllowed.Add(key);
                    continue;
                }
                switch (filter.Kind)
                {
                    case LookupFilterKind.Id when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0:
                        result[filter.Key] = id.ToString(CultureInfo.InvariantCulture);
                        break;
                    case LookupFilterKind.Flag when bool.TryParse(value, out var flag):
                        result[filter.Key] = flag ? "true" : "false";
                        break;
                    case LookupFilterKind.Prefer:
                        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var prefer) && prefer > 0)
                            result[filter.Key] = prefer.ToString(CultureInfo.InvariantCulture);
                        break;
                    default:
                        invalid.Add(filter.Key);
                        break;
                }
            }

            if (notAllowed.Count > 0)
            {
                throw new LookupFilterException(LookupFilterException.NotAllowed, notAllowed,
                    $"A {type.TypeName} lookup can't be narrowed by {string.Join(", ", notAllowed)}; only the full {type.TypeName} search can.");
            }
            if (invalid.Count > 0)
            {
                throw new LookupFilterException(LookupFilterException.Invalid, invalid,
                    $"The {type.TypeName} lookup filter {string.Join(", ", invalid)} needs "
                    + (invalid.All(k => lookup.FilterFor(k)!.Kind == LookupFilterKind.Flag) ? "true or false." : "a record id (a positive number)."));
            }
            return result;
        }

        private static bool IsPrefer(string key) => key.StartsWith("prefer", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>A lookup search's filters were refused (PandiO/knk-workspace#288): the controller answers 400.</summary>
    public class LookupFilterException : ArgumentException
    {
        public const string NotAllowed = "LookupFilterNotAllowed";
        public const string Invalid = "LookupFilterInvalid";

        public LookupFilterException(string code, IReadOnlyList<string> filters, string message) : base(message)
        {
            Code = code;
            Filters = filters;
        }

        /// <summary><see cref="NotAllowed"/> or <see cref="Invalid"/>.</summary>
        public string Code { get; }

        /// <summary>The refused keys, as sent (not allowed) or as the type spells them (invalid).</summary>
        public IReadOnlyList<string> Filters { get; }
    }
}
