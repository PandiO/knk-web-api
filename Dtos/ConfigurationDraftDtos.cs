using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// PandiO/knk-workspace#71: what the form builder opens. <see cref="Configuration"/> is the working copy (the saved
    /// draft when there is one, else the published rows); <see cref="Published"/> is what the form wizard reads now
    /// (null while the configuration was never published).
    /// </summary>
    public class FormConfigurationBuilderStateDto
    {
        [JsonPropertyName("configuration")]
        public FormConfigurationDto Configuration { get; set; } = null!;

        [JsonPropertyName("published")]
        public FormConfigurationDto? Published { get; set; }

        /// <summary>False while never published (created in the builder or duplicated).</summary>
        [JsonPropertyName("isPublished")]
        public bool IsPublished { get; set; }

        /// <summary>A saved working copy differs from the published version (or nothing was published yet).</summary>
        [JsonPropertyName("hasUnpublishedChanges")]
        public bool HasUnpublishedChanges { get; set; }

        [JsonPropertyName("draftSavedAt")]
        public string? DraftSavedAt { get; set; }

        [JsonPropertyName("publishedAt")]
        public string? PublishedAt { get; set; }

        /// <summary>Saved wizard drafts (in progress or paused) started on this configuration.</summary>
        [JsonPropertyName("openDraftCount")]
        public int OpenDraftCount { get; set; }

        /// <summary>The entity's published default configuration, when it is another one (Publish as default replaces it).</summary>
        [JsonPropertyName("currentDefaultId")]
        public string? CurrentDefaultId { get; set; }

        [JsonPropertyName("currentDefaultName")]
        public string? CurrentDefaultName { get; set; }
    }

    /// <summary>#71: what the display builder opens; see <see cref="FormConfigurationBuilderStateDto"/>.</summary>
    public class DisplayConfigurationBuilderStateDto
    {
        [JsonPropertyName("configuration")]
        public DisplayConfigurationDto Configuration { get; set; } = null!;

        [JsonPropertyName("published")]
        public DisplayConfigurationDto? Published { get; set; }

        [JsonPropertyName("isPublished")]
        public bool IsPublished { get; set; }

        [JsonPropertyName("hasUnpublishedChanges")]
        public bool HasUnpublishedChanges { get; set; }

        [JsonPropertyName("draftSavedAt")]
        public string? DraftSavedAt { get; set; }

        [JsonPropertyName("publishedAt")]
        public string? PublishedAt { get; set; }

        [JsonPropertyName("currentDefaultId")]
        public string? CurrentDefaultId { get; set; }

        [JsonPropertyName("currentDefaultName")]
        public string? CurrentDefaultName { get; set; }
    }

    /// <summary>#71: <c>POST {id}/duplicate</c>. Without a name the copy is called "&lt;name&gt; (copy)".</summary>
    public class DuplicateConfigurationRequestDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
