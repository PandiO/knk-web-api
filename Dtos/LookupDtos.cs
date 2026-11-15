using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// POST api/Lookup/{entityType}/search (PandiO/knk-workspace#280 AM2, DESIGN.md § 4.6.3): the picker's search box.
    /// A lookup finds by the type's own search term (users: the username), narrowed by the relation field's
    /// <c>pickerFilters</c> where the type's own search accepts them (PandiO/knk-workspace#288). No sort.
    /// </summary>
    public class LookupSearchRequestDto
    {
        [JsonPropertyName("searchTerm")]
        public string? SearchTerm { get; set; }

        [JsonPropertyName("page")]
        public int Page { get; set; } = 1;

        /// <summary>1–50; larger values are cut to 50.</summary>
        [JsonPropertyName("pageSize")]
        public int PageSize { get; set; } = 20;

        /// <summary>
        /// Narrowing filters (PandiO/knk-workspace#288), e.g. <c>{ "siegeScenarioId": "3" }</c> for a siege team picker:
        /// only the keys the type's lookup lists (<see cref="knkwebapi_v2.Attributes.DashboardLookup.Filters"/>), keys
        /// matched without case. Another key answers 400 <c>LookupFilterNotAllowed</c>, a value the filter can't use
        /// (an id that isn't a positive number, a flag that isn't true/false) 400 <c>LookupFilterInvalid</c>, so a
        /// lookup never answers with a wider list than the form asked for. <c>prefer…</c> keys only order the results:
        /// one the type doesn't take, or with an unusable value, is left out.
        /// </summary>
        [JsonPropertyName("filters")]
        public Dictionary<string, string?>? Filters { get; set; }
    }

    /// <summary>One record as a picker or a relation label shows it: never more than the id, a label and one short line.</summary>
    public class LookupItemDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("label")]
        public string Label { get; set; } = null!;

        [JsonPropertyName("detail")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Detail { get; set; }
    }

    public class LookupSearchResponseDto
    {
        [JsonPropertyName("items")]
        public List<LookupItemDto> Items { get; set; } = new();

        [JsonPropertyName("total")]
        public int Total { get; set; }
    }
}
