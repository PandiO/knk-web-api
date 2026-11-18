using System;
using System.Text.Json.Serialization;
using knkwebapi_v2.Json;

namespace knkwebapi_v2.Dtos
{
    // Off-road navigation phase 5.1 (PandiO/knk-workspace#708, DESIGN § 6.3, § 6.4). The read DTO carries everything the
    // plugin's landmark provider needs (destination engine DESIGN § 3.2): id, name, the world and point of the pin,
    // domain id and name, the extent and the flags, so the plugin side needs no API change.

    /// <summary>A landmark as <c>GET api/landmarks</c>, <c>GET api/landmarks/{id}</c> and create return it.</summary>
    public class LandmarkReadDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("locationId")]
        public int LocationId { get; set; }

        /// <summary>The pin (id, name, x, y, z, yaw, pitch, world).</summary>
        [JsonPropertyName("location")]
        public LocationDto? Location { get; set; }

        /// <summary>The pin's world, also flat here so a caller can group without the Location.</summary>
        [JsonPropertyName("world")]
        public string? World { get; set; }

        [JsonPropertyName("domainId")]
        public int? DomainId { get; set; }

        [JsonPropertyName("domain")]
        public LandmarkDomainNavDto? Domain { get; set; }

        /// <summary>The linked domain's name (the plugin's "in &lt;domain&gt;" detail); null when none.</summary>
        [JsonPropertyName("domainName")]
        public string? DomainName { get; set; }

        /// <summary><c>None</c>, <c>Region</c> or <c>Radius</c>.</summary>
        [JsonPropertyName("extentKind")]
        public string ExtentKind { get; set; } = "None";

        [JsonPropertyName("regionId")]
        public string? RegionId { get; set; }

        [JsonPropertyName("radius")]
        public double? Radius { get; set; }

        [JsonPropertyName("preference")]
        public double Preference { get; set; }

        [JsonPropertyName("navigationEnabled")]
        public bool NavigationEnabled { get; set; }

        /// <summary><c>General</c> (the only kind in v1).</summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "General";

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>A row of <c>POST api/landmarks/search</c> (the dashboard table and the domain pages' landmark list).</summary>
    public class LandmarkListDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("world")]
        public string? World { get; set; }

        [JsonPropertyName("x")]
        public double? X { get; set; }

        [JsonPropertyName("y")]
        public double? Y { get; set; }

        [JsonPropertyName("z")]
        public double? Z { get; set; }

        [JsonPropertyName("domainId")]
        public int? DomainId { get; set; }

        [JsonPropertyName("domainName")]
        public string? DomainName { get; set; }

        [JsonPropertyName("domainType")]
        public string? DomainType { get; set; }

        [JsonPropertyName("extentKind")]
        public string ExtentKind { get; set; } = "None";

        [JsonPropertyName("preference")]
        public double Preference { get; set; }

        [JsonPropertyName("navigationEnabled")]
        public bool NavigationEnabled { get; set; }

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "General";
    }

    /// <summary>A landmark as another DTO nests it.</summary>
    public class LandmarkNavDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>The linked domain as a landmark nests it: no full domain DTO, so there are no cycles.</summary>
    public class LandmarkDomainNavDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary><c>Town</c>, <c>District</c>, <c>Structure</c>, <c>GateStructure</c>, … (the CLR type name).</summary>
        [JsonPropertyName("domainType")]
        public string DomainType { get; set; } = string.Empty;

        [JsonPropertyName("worldName")]
        public string? WorldName { get; set; }
    }

    /// <summary>
    /// The body of <c>POST api/landmarks</c>. The pin is a <c>locationId</c> or an embedded <c>location</c> (no id =
    /// create, with id = update), like a domain's spawn point (KNG-195). Omitted optional fields take their defaults
    /// (extent <c>None</c>, preference 1.0, navigation on, kind <c>General</c>).
    /// </summary>
    public class LandmarkCreateDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("locationId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? LocationId { get; set; }

        [JsonPropertyName("location")]
        public LocationDto? Location { get; set; }

        [JsonPropertyName("domainId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? DomainId { get; set; }

        [JsonPropertyName("extentKind")]
        public string? ExtentKind { get; set; }

        [JsonPropertyName("regionId")]
        public string? RegionId { get; set; }

        [JsonPropertyName("radius")]
        [JsonConverter(typeof(NullableDoubleConverter))]
        public double? Radius { get; set; }

        [JsonPropertyName("preference")]
        [JsonConverter(typeof(NullableDoubleConverter))]
        public double? Preference { get; set; }

        [JsonPropertyName("navigationEnabled")]
        public bool? NavigationEnabled { get; set; }

        [JsonPropertyName("kind")]
        public string? Kind { get; set; }
    }

    /// <summary>
    /// The body of <c>PUT api/landmarks/{id}</c>, as the form and <c>/knk landmark</c> send it. The route id wins over a
    /// body id. <c>name</c> is required and <c>domainId</c> is taken as sent (null unlinks). The pin, the extent
    /// (<c>extentKind</c> with <c>regionId</c>/<c>radius</c>), <c>preference</c>, <c>navigationEnabled</c> and <c>kind</c>
    /// keep their stored values when null.
    /// </summary>
    public class LandmarkUpdateDto : LandmarkCreateDto
    {
        [JsonPropertyName("id")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? Id { get; set; }
    }
}
