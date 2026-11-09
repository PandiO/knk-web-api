using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    public class EntityMetadataDto
    {
        [JsonPropertyName("entityName")]
        public string EntityName { get; set; } = null!;
        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = null!;
        [JsonPropertyName("fields")]
        public List<FieldMetadataDto> Fields { get; set; } = new();
        [JsonPropertyName("defaultTableColumns")]
        public List<string>? DefaultTableColumns { get; set; }

        // Dashboard access model (PandiO/knk-workspace#280, DESIGN.md § 4.2), from Attributes/DashboardAccess, so the
        // web app never hard-codes a node per type. Null/false for a type outside the registry.

        /// <summary>The type's area: Content, World, Kits, Lootboxes, Siege, Roads or Players.</summary>
        [JsonPropertyName("area")]
        public string? Area { get; set; }

        /// <summary>The area node: view, create and edit (and who gets the type's live updates).</summary>
        [JsonPropertyName("accessNode")]
        public string? AccessNode { get; set; }

        /// <summary>What create and edit need: the area node, except permission groups (knk.admin.user.perm).</summary>
        [JsonPropertyName("writeNode")]
        public string? WriteNode { get; set; }

        /// <summary>Any one of these lets a user read the type in full (area node plus cross-area page nodes).</summary>
        [JsonPropertyName("readNodes")]
        public List<string> ReadNodes { get; set; } = new();

        [JsonPropertyName("deletable")]
        public bool Deletable { get; set; }

        [JsonPropertyName("bulkDeletable")]
        public bool BulkDeletable { get; set; }

        /// <summary>POST api/Dashboard/export can read this type.</summary>
        [JsonPropertyName("exportable")]
        public bool Exportable { get; set; }

        /// <summary>POST api/Lookup/{entityName}/search answers for this type (AM2: pickers without a full read).</summary>
        [JsonPropertyName("lookupSearchable")]
        public bool LookupSearchable { get; set; }

        /// <summary>GET api/Lookup/{entityName}/{id} answers for this type (AM2: relation labels without a full read).</summary>
        [JsonPropertyName("lookupGettable")]
        public bool LookupGettable { get; set; }

        /// <summary>
        /// For each action the type allows, every node it needs (all of them): <c>view</c> (the dashboard lists the
        /// type: its area node), <c>create</c>, <c>edit</c>, <c>delete</c>, <c>bulkDelete</c>, <c>export</c>. An action
        /// the type doesn't allow is left out. A full read from another page is <see cref="ReadNodes"/> (any one).
        /// </summary>
        [JsonPropertyName("actionNodes")]
        public Dictionary<string, List<string>> ActionNodes { get; set; } = new();

        /// <summary>
        /// The type's relationships for the entity detail page (PandiO/knk-workspace#188): what each navigation links to,
        /// whether Select/Add and Unlink work through <c>api/Relationships</c>, why not, and the nodes a change needs.
        /// </summary>
        [JsonPropertyName("relationships")]
        public List<RelationshipMetadataDto> Relationships { get; set; } = new();
    }

    public class FieldMetadataDto
    {
        [JsonPropertyName("fieldName")]
        public string FieldName { get; set; } = null!;
        [JsonPropertyName("fieldType")]
        public string FieldType { get; set; } = null!;
        [JsonPropertyName("isNullable")]
        public bool IsNullable { get; set; }
        [JsonPropertyName("isRelatedEntity")]
        public bool IsRelatedEntity { get; set; }
        [JsonPropertyName("relatedEntityType")]
        public string? RelatedEntityType { get; set; }
        /// <summary>
        /// Indicates if this field has a default value defined in the model class.
        /// If true, the field can be optional in forms even if non-nullable on the entity.
        /// Example: public string Name { get; set; } = null!; (has default, though typically null)
        /// </summary>
        [JsonPropertyName("hasDefaultValue")]
        public bool HasDefaultValue { get; set; }
        /// <summary>
        /// The default value as a string representation, if one exists.
        /// Examples: "True", "False", "null", "DateTime.Now", "0", "true"
        /// </summary>
        [JsonPropertyName("defaultValue")]
        public string? DefaultValue { get; set; }

        [JsonPropertyName("isEnum")]
        public bool IsEnum { get; set; }

        [JsonPropertyName("enumTypeName")]
        public string? EnumTypeName { get; set; }

        /// <summary>
        /// The names of the enum members, in declaration order. Lets the form builder offer real
        /// options instead of a hand-typed list.
        /// </summary>
        [JsonPropertyName("enumValues")]
        public List<string>? EnumValues { get; set; }
    }
}
