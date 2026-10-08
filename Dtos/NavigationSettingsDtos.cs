using System;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// A domain DTO that carries the per-domain <c>/navigate</c> default (KNG-73). On create/update
    /// null means "not on this form" (left as it is); "" or "TypeDefault" clears the override so the
    /// domain follows its type; "Spawn" / "Region" set it. Applied by DomainNavigationDefaults.Apply.
    /// </summary>
    public interface IDomainNavigationDefaultDto
    {
        string? NavigationDefaultOverride { get; set; }
    }

    /// <summary>One domain type's <c>/navigate</c> default (api/navigation-settings/domain-defaults).</summary>
    public class DomainNavigationDefaultDto
    {
        [JsonPropertyName("domainType")]
        public string DomainType { get; set; } = "";

        /// <summary>"Spawn" or "Region".</summary>
        [JsonPropertyName("defaultMode")]
        public string DefaultMode { get; set; } = "";

        /// <summary>Domains of this type that override the type default.</summary>
        [JsonPropertyName("overrideCount")]
        public int OverrideCount { get; set; }

        [JsonPropertyName("updatedAt")]
        public DateTime? UpdatedAt { get; set; }
    }

    public class UpdateDomainNavigationDefaultDto
    {
        /// <summary>"Spawn" or "Region" (any case).</summary>
        [JsonPropertyName("defaultMode")]
        public string? DefaultMode { get; set; }
    }
}
