using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>POST api/Dashboard/bulk-delete (PandiO/knk-workspace#280, DESIGN.md § 4.7): one type, at most 100 ids.</summary>
    public class DashboardBulkDeleteRequestDto
    {
        [JsonPropertyName("entityType")]
        public string? EntityType { get; set; }

        [JsonPropertyName("ids")]
        public List<int>? Ids { get; set; }
    }

    /// <summary>One id the bulk delete left alone, with the reason a single delete would have given.</summary>
    public class DashboardBulkDeleteRefusalDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = null!;
    }

    public class DashboardBulkDeleteResponseDto
    {
        [JsonPropertyName("deleted")]
        public List<int> Deleted { get; set; } = new();

        [JsonPropertyName("refused")]
        public List<DashboardBulkDeleteRefusalDto> Refused { get; set; } = new();
    }

    /// <summary>The search an export reads: the dashboard table's own search, sort and filters (all pages).</summary>
    public class DashboardExportQueryDto
    {
        [JsonPropertyName("searchTerm")]
        public string? SearchTerm { get; set; }

        [JsonPropertyName("filters")]
        public Dictionary<string, string>? Filters { get; set; }

        [JsonPropertyName("sortBy")]
        public string? SortBy { get; set; }

        [JsonPropertyName("sortDescending")]
        public bool SortDescending { get; set; }
    }

    /// <summary>
    /// POST api/Dashboard/export (DESIGN.md § 4.8): the selected <c>ids</c>, or with none the whole <c>query</c>. With
    /// both, the selected rows come in the query's order.
    /// </summary>
    public class DashboardExportRequestDto
    {
        [JsonPropertyName("entityType")]
        public string? EntityType { get; set; }

        [JsonPropertyName("ids")]
        public List<int>? Ids { get; set; }

        [JsonPropertyName("query")]
        public DashboardExportQueryDto? Query { get; set; }
    }

    /// <summary>The rows as the type's search returns them (the table formats them into the CSV).</summary>
    public class DashboardExportResponseDto
    {
        [JsonPropertyName("items")]
        public List<object> Items { get; set; } = new();

        /// <summary>Rows in the scope: the search's total, or the selected ids found.</summary>
        [JsonPropertyName("total")]
        public int Total { get; set; }

        /// <summary>True when the scope holds more rows than were returned (the 10 000-row cap).</summary>
        [JsonPropertyName("truncated")]
        public bool Truncated { get; set; }
    }
}
