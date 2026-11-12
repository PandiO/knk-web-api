using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// One relationship of a dashboard type as the entity detail page uses it (PandiO/knk-workspace#188): what the
    /// property links to, whether Select/Add and Unlink are possible, why not, and the nodes the change needs. Part of
    /// <see cref="EntityMetadataDto.Relationships"/>.
    /// </summary>
    public class RelationshipMetadataDto
    {
        /// <summary>The navigation property, camelCase as the read DTOs name it (<c>streets</c>, <c>parentCategory</c>).</summary>
        [JsonPropertyName("property")]
        public string Property { get; set; } = null!;

        /// <summary>The type the page links to: the related entity, or for a link row (<c>ItemBlueprintTag</c>) the other side (<c>Tag</c>).</summary>
        [JsonPropertyName("relatedType")]
        public string RelatedType { get; set; } = null!;

        /// <summary><c>reference</c> (one related entity) or <c>collection</c>.</summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = null!;

        /// <summary>
        /// How the API stores it: <c>foreignKey</c> (this entity's own column), <c>children</c> (the related row's column),
        /// <c>many</c> (a hidden link table) or <c>linkRow</c> (a link entity with only the two ids).
        /// </summary>
        [JsonPropertyName("storage")]
        public string Storage { get; set; } = null!;

        /// <summary>Select (reference) or Add (collection) works through <c>api/Relationships</c>.</summary>
        [JsonPropertyName("canLink")]
        public bool CanLink { get; set; }

        /// <summary>Unlink works: the related entity is kept, only the link goes.</summary>
        [JsonPropertyName("canUnlink")]
        public bool CanUnlink { get; set; }

        /// <summary>Why Select/Add isn't offered (shown next to the disabled button).</summary>
        [JsonPropertyName("linkReason")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LinkReason { get; set; }

        /// <summary>Why Unlink isn't offered.</summary>
        [JsonPropertyName("unlinkReason")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? UnlinkReason { get; set; }

        /// <summary>Every one of these is needed for a change (all-of, like <c>actionNodes</c>).</summary>
        [JsonPropertyName("nodes")]
        public List<string> Nodes { get; set; } = new();

        /// <summary>
        /// For <c>linkRow</c>: the item property holding the other side's id (<c>tagId</c>) and the embedded other side
        /// (<c>tag</c>); the item's own <c>id</c> otherwise.
        /// </summary>
        [JsonPropertyName("relatedIdField")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RelatedIdField { get; set; }

        [JsonPropertyName("relatedNavigation")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RelatedNavigation { get; set; }
    }

    /// <summary>
    /// <c>POST api/Relationships/{entityType}/{id}/{property}/link</c>: Select (reference) or Add (collection) an existing
    /// entity. <c>move</c> confirms taking a child away from the parent it has now (the 409 <c>AlreadyLinked</c> answer).
    /// </summary>
    public class RelationshipLinkRequestDto
    {
        [JsonPropertyName("relatedId")]
        public int RelatedId { get; set; }

        [JsonPropertyName("move")]
        public bool Move { get; set; }
    }

    /// <summary><c>POST …/unlink</c>: the related id is required for a collection, ignored for a reference.</summary>
    public class RelationshipUnlinkRequestDto
    {
        [JsonPropertyName("relatedId")]
        public int? RelatedId { get; set; }
    }

    public class RelationshipChangeResultDto
    {
        [JsonPropertyName("entityType")]
        public string EntityType { get; set; } = null!;

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("property")]
        public string Property { get; set; } = null!;

        [JsonPropertyName("relatedId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? RelatedId { get; set; }

        /// <summary>False when the link already was (or wasn't) there: nothing was saved.</summary>
        [JsonPropertyName("changed")]
        public bool Changed { get; set; }

        /// <summary>For a moved child or a replaced reference: the id it pointed to before.</summary>
        [JsonPropertyName("previousId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? PreviousId { get; set; }
    }
}
