using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// GET api/EntityReferences/{entityType}/{id}/rows (PandiO/knk-workspace#44): what still uses one record, row by row,
    /// for a refused delete's "Show details". Only the references that stop a delete (the refusal message's rule), grouped
    /// as the refusal counts them.
    /// </summary>
    public class EntityReferenceRowsDto
    {
        [JsonPropertyName("entityTypeName")]
        public string EntityTypeName { get; set; } = null!;

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("groups")]
        public List<EntityReferenceGroupDto> Groups { get; set; } = new();
    }

    /// <summary>
    /// One reference ("Structure.StreetId", "District (link)") with up to <c>take</c> rows. Rows are id + label (+ one
    /// detail line) through the lookup labels, never more: <see cref="Readable"/> says whether the caller may open them
    /// (one of the type's read nodes); without it the rows are still named when the caller holds any area node (as
    /// <c>api/Lookup</c> would), otherwise only counted (<see cref="Listed"/> false, <see cref="Nodes"/> say what reads them).
    /// </summary>
    public class EntityReferenceGroupDto
    {
        /// <summary>As in the refusal's <c>references</c>: "Structure.StreetId", or "District (link)" for a join row.</summary>
        [JsonPropertyName("referencedBy")]
        public string ReferencedBy { get; set; } = null!;

        /// <summary>The type of the rows (<c>Structure</c>); null when a row has no single id.</summary>
        [JsonPropertyName("entityType")]
        public string? EntityType { get; set; }

        /// <summary>Every referencing row, also those not listed.</summary>
        [JsonPropertyName("count")]
        public int Count { get; set; }

        /// <summary>Whether <see cref="Rows"/> names the rows (false: count only, see <see cref="Reason"/>).</summary>
        [JsonPropertyName("listed")]
        public bool Listed { get; set; }

        /// <summary>The caller holds one of the type's read nodes: the web app links each row to its page.</summary>
        [JsonPropertyName("readable")]
        public bool Readable { get; set; }

        /// <summary>The type's read nodes (any one), for "Opening … requires one of: …".</summary>
        [JsonPropertyName("nodes")]
        public List<string> Nodes { get; set; } = new();

        /// <summary>Why the rows aren't listed, in words.</summary>
        [JsonPropertyName("reason")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Reason { get; set; }

        [JsonPropertyName("rows")]
        public List<LookupItemDto> Rows { get; set; } = new();

        /// <summary>More rows than <see cref="Rows"/> holds (<c>take</c> was reached).</summary>
        [JsonPropertyName("more")]
        public bool More { get; set; }
    }
}
