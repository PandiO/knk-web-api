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

        /// <summary>Rev. 7 Part C (KNG-92): null leaves it, "" / "TypeDefault" clears it, "Applies" / "Ignored" set it.</summary>
        string? RoadAccessOverride { get; set; }
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

        /// <summary>"Applies" or "Ignored": whether the type's entry/exit rule keeps routes off its roads (rev. 7 Part C, KNG-92).</summary>
        [JsonPropertyName("roadAccess")]
        public string RoadAccess { get; set; } = "";

        /// <summary>Domains of this type with their own road-access choice.</summary>
        [JsonPropertyName("roadAccessOverrideCount")]
        public int RoadAccessOverrideCount { get; set; }

        [JsonPropertyName("updatedAt")]
        public DateTime? UpdatedAt { get; set; }
    }

    public class UpdateDomainNavigationDefaultDto
    {
        /// <summary>"Spawn" or "Region" (any case); null leaves it.</summary>
        [JsonPropertyName("defaultMode")]
        public string? DefaultMode { get; set; }

        /// <summary>"Applies" or "Ignored" (any case); null leaves it. At least one of the two is required.</summary>
        [JsonPropertyName("roadAccess")]
        public string? RoadAccess { get; set; }
    }
}

namespace knkwebapi_v2.Dtos
{
    /// <summary>The navigation compass settings (api/navigation-settings/compass, KNG-234).</summary>
    public class NavigationCompassSettingsDto
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        /// <summary>The compass item's material, e.g. "COMPASS".</summary>
        [JsonPropertyName("material")]
        public string Material { get; set; } = "";

        /// <summary>The display name to match (colour codes ignored); empty matches any name.</summary>
        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = "";

        /// <summary>"Exact" or "Contains".</summary>
        [JsonPropertyName("nameMatch")]
        public string NameMatch { get; set; } = "";

        [JsonPropertyName("siegeEnabled")]
        public bool SiegeEnabled { get; set; }

        [JsonPropertyName("generalEnabled")]
        public bool GeneralEnabled { get; set; }

        /// <summary>Destinations outside a siege, one /navigate argument per line.</summary>
        [JsonPropertyName("generalTargets")]
        public string GeneralTargets { get; set; } = "";

        /// <summary>Null while nothing was ever saved (the defaults are shown).</summary>
        [JsonPropertyName("updatedAt")]
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>A partial update: null leaves a field as it is.</summary>
    public class UpdateNavigationCompassSettingsDto
    {
        [JsonPropertyName("enabled")]
        public bool? Enabled { get; set; }

        [JsonPropertyName("material")]
        public string? Material { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("nameMatch")]
        public string? NameMatch { get; set; }

        [JsonPropertyName("siegeEnabled")]
        public bool? SiegeEnabled { get; set; }

        [JsonPropertyName("generalEnabled")]
        public bool? GeneralEnabled { get; set; }

        [JsonPropertyName("generalTargets")]
        public string? GeneralTargets { get; set; }
    }
}
