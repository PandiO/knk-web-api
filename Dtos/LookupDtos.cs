using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// POST api/Lookup/{entityType}/search (PandiO/knk-workspace#280 AM2, DESIGN.md § 4.6.3): the picker's search box.
    /// No filters or sort: a lookup finds by the type's own search term (users: the username).
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
