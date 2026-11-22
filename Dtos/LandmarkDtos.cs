using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using knkwebapi_v2.Json;

namespace knkwebapi_v2.Dtos
{
    // Landmarks as a Domain subtype (landmark-domain DESIGN § 6.2 item 1, PandiO/knk-workspace#983; first built by off-road
    // #708). The DTOs carry the landmark fields (area kind, parent, preference, navigation, kind, messages) and the domain
    // fields (region, sub-regions, access, warp).
    //
    // Compatibility until the plugin moves to this model (link L3, #985): knk-plugin's LandmarkDto reads `domainId`,
    // `domain`, `domainName`, `extentKind` ("None", "Region", "Radius"), `regionId` and `radius`, and `/knk landmark`
    // writes them. The read DTOs keep those names as a projection of the new model (`extentKind` "Region" with the
    // area's main region: the own region, or the parent's main region for LinkedDomain; `radius` always null), and the
    // write DTOs still accept them when the new fields are not sent (agent decision of L1, see LandmarkService).

    /// <summary>A landmark as <c>GET api/landmarks</c>, <c>GET api/landmarks/{id}</c>, create and update return it.</summary>
    public class LandmarkReadDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>Always <c>Landmark</c>: the domain type, as <c>api/Domains</c> names it.</summary>
        [JsonPropertyName("domainType")]
        public string DomainType { get; set; } = "Landmark";

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("locationId")]
        public int LocationId { get; set; }

        /// <summary>The pin (id, name, x, y, z, yaw, pitch, world).</summary>
        [JsonPropertyName("location")]
        public LocationDto? Location { get; set; }

        /// <summary>The pin's world, also flat here so a caller can group without the Location.</summary>
        [JsonPropertyName("world")]
        public string? World { get; set; }

        /// <summary>The domain's world (the pin's world).</summary>
        [JsonPropertyName("worldName")]
        public string? WorldName { get; set; }

        /// <summary><c>None</c>, <c>OwnRegion</c> or <c>LinkedDomain</c> (LD16).</summary>
        [JsonPropertyName("areaKind")]
        public string AreaKind { get; set; } = "None";

        /// <summary>The landmark's own main region (<c>domain_&lt;id&gt;</c> once drawn); null unless <c>OwnRegion</c>.</summary>
        [JsonPropertyName("wgRegionId")]
        public string? WgRegionId { get; set; }

        /// <summary>The ids of the landmark's own sub-regions, in order (only with <c>OwnRegion</c>).</summary>
        [JsonPropertyName("subRegionIds")]
        public List<string> SubRegionIds { get; set; } = new();

        [JsonPropertyName("subRegions")]
        public List<DomainSubRegionDto>? SubRegions { get; set; }

        /// <summary>The parent (the linked domain, LD09); null for a top-level landmark.</summary>
        [JsonPropertyName("parentDomainId")]
        public int? ParentDomainId { get; set; }

        [JsonPropertyName("parentDomain")]
        public LandmarkDomainNavDto? ParentDomain { get; set; }

        [JsonPropertyName("parentDomainName")]
        public string? ParentDomainName { get; set; }

        [JsonPropertyName("allowEntry")]
        public bool AllowEntry { get; set; } = true;

        [JsonPropertyName("allowExit")]
        public bool AllowExit { get; set; } = true;

        [JsonPropertyName("showEnterLeaveMessages")]
        public bool ShowEnterLeaveMessages { get; set; }

        [JsonPropertyName("teleportEnabled")]
        public bool TeleportEnabled { get; set; }

        [JsonPropertyName("teleportPriceGems")]
        public int TeleportPriceGems { get; set; }

        [JsonPropertyName("teleportMinTitleBracketId")]
        public int? TeleportMinTitleBracketId { get; set; }

        [JsonPropertyName("teleportMinPremiumGroupId")]
        public int? TeleportMinPremiumGroupId { get; set; }

        [JsonPropertyName("teleportRequiresDiscovery")]
        public bool TeleportRequiresDiscovery { get; set; }

        [JsonPropertyName("preference")]
        public double Preference { get; set; }

        [JsonPropertyName("navigationEnabled")]
        public bool NavigationEnabled { get; set; }

        /// <summary><c>General</c> (the only kind in v1).</summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "General";

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        // ===== Compatibility with knk-plugin before L3 (#985): the #708 field names, derived from the fields above =====

        /// <summary>Compatibility (#708 name): the parent's id, as <see cref="ParentDomainId"/>.</summary>
        [JsonPropertyName("domainId")]
        public int? DomainId { get; set; }

        /// <summary>Compatibility (#708 name): the parent, as <see cref="ParentDomain"/>.</summary>
        [JsonPropertyName("domain")]
        public LandmarkDomainNavDto? Domain { get; set; }

        /// <summary>Compatibility (#708 name): the parent's name, as <see cref="ParentDomainName"/>.</summary>
        [JsonPropertyName("domainName")]
        public string? DomainName { get; set; }

        /// <summary>Compatibility (#708 name): <c>Region</c> when the area has a main region (own, or the parent's for
        /// <c>LinkedDomain</c>), else <c>None</c>. Never <c>Radius</c> (LD08).</summary>
        [JsonPropertyName("extentKind")]
        public string ExtentKind { get; set; } = "None";

        /// <summary>Compatibility (#708 name): the area's main region (own, or the parent's); null for a point.</summary>
        [JsonPropertyName("regionId")]
        public string? RegionId { get; set; }

        /// <summary>Compatibility (#708 name): always null, the radius extent is gone (LD08).</summary>
        [JsonPropertyName("radius")]
        public double? Radius { get; set; }
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

        [JsonPropertyName("areaKind")]
        public string AreaKind { get; set; } = "None";

        [JsonPropertyName("wgRegionId")]
        public string? WgRegionId { get; set; }

        [JsonPropertyName("parentDomainId")]
        public int? ParentDomainId { get; set; }

        [JsonPropertyName("parentDomainName")]
        public string? ParentDomainName { get; set; }

        [JsonPropertyName("parentDomainType")]
        public string? ParentDomainType { get; set; }

        [JsonPropertyName("preference")]
        public double Preference { get; set; }

        [JsonPropertyName("navigationEnabled")]
        public bool NavigationEnabled { get; set; }

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "General";

        // Compatibility (#708 names: the web app's landmark list and domain panel until L4, #986).

        [JsonPropertyName("domainId")]
        public int? DomainId { get; set; }

        [JsonPropertyName("domainName")]
        public string? DomainName { get; set; }

        [JsonPropertyName("domainType")]
        public string? DomainType { get; set; }

        [JsonPropertyName("extentKind")]
        public string ExtentKind { get; set; } = "None";
    }

    /// <summary>A landmark as another DTO nests it.</summary>
    public class LandmarkNavDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>The parent domain as a landmark nests it: no full domain DTO, so there are no cycles.</summary>
    public class LandmarkDomainNavDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary><c>Town</c>, <c>District</c>, <c>Structure</c>, <c>GateStructure</c>, <c>Landmark</c>, … (the CLR type name).</summary>
        [JsonPropertyName("domainType")]
        public string DomainType { get; set; } = string.Empty;

        [JsonPropertyName("worldName")]
        public string? WorldName { get; set; }

        [JsonPropertyName("wgRegionId")]
        public string? WgRegionId { get; set; }
    }

    /// <summary>
    /// The body of <c>POST api/landmarks</c>. The pin is a <c>locationId</c> or an embedded <c>location</c> (no id =
    /// create, with id = update), like a domain's spawn point (KNG-195); a Location another domain already uses is copied
    /// (LD17). Omitted optional fields take their defaults (area <c>None</c>, no parent, entry and exit allowed, messages
    /// off, warp off, preference 1.0, navigation on, kind <c>General</c>). The #708 fields <c>domainId</c>,
    /// <c>extentKind</c> and <c>regionId</c> are read when <c>parentDomainId</c>, <c>areaKind</c> and <c>wgRegionId</c>
    /// are not sent (knk-plugin before L3); a <c>radius</c> is refused.
    /// </summary>
    public class LandmarkCreateDto : IDomainTeleportSettingsDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("locationId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? LocationId { get; set; }

        [JsonPropertyName("location")]
        public LocationDto? Location { get; set; }

        /// <summary><c>None</c>, <c>OwnRegion</c> or <c>LinkedDomain</c>.</summary>
        [JsonPropertyName("areaKind")]
        public string? AreaKind { get; set; }

        /// <summary>The own region with <c>OwnRegion</c>: a region drawn through the form's region task (renamed to
        /// <c>domain_&lt;id&gt;</c> on save) or an existing region no other domain holds.</summary>
        [JsonPropertyName("wgRegionId")]
        public string? WgRegionId { get; set; }

        [JsonPropertyName("parentDomainId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? ParentDomainId { get; set; }

        [JsonPropertyName("allowEntry")]
        public bool? AllowEntry { get; set; }

        [JsonPropertyName("allowExit")]
        public bool? AllowExit { get; set; }

        [JsonPropertyName("showEnterLeaveMessages")]
        public bool? ShowEnterLeaveMessages { get; set; }

        [JsonPropertyName("teleportEnabled")]
        public bool? TeleportEnabled { get; set; }

        [JsonPropertyName("teleportPriceGems")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? TeleportPriceGems { get; set; }

        [JsonPropertyName("teleportMinTitleBracketId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? TeleportMinTitleBracketId { get; set; }

        [JsonPropertyName("teleportMinPremiumGroupId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? TeleportMinPremiumGroupId { get; set; }

        [JsonPropertyName("teleportRequiresDiscovery")]
        public bool? TeleportRequiresDiscovery { get; set; }

        [JsonPropertyName("preference")]
        [JsonConverter(typeof(NullableDoubleConverter))]
        public double? Preference { get; set; }

        [JsonPropertyName("navigationEnabled")]
        public bool? NavigationEnabled { get; set; }

        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        // ===== Compatibility with knk-plugin before L3 (#985) =====

        /// <summary>Compatibility (#708 name) for <see cref="ParentDomainId"/>, read when that is not sent.</summary>
        [JsonPropertyName("domainId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? DomainId { get; set; }

        /// <summary>Compatibility (#708 name) for <see cref="AreaKind"/>: <c>None</c> or <c>Region</c> (with
        /// <see cref="RegionId"/>); <c>Radius</c> is refused (LD08).</summary>
        [JsonPropertyName("extentKind")]
        public string? ExtentKind { get; set; }

        /// <summary>Compatibility (#708 name) for <see cref="WgRegionId"/> with <c>extentKind</c> <c>Region</c>.</summary>
        [JsonPropertyName("regionId")]
        public string? RegionId { get; set; }

        /// <summary>Compatibility (#708 name): refused when set, the radius extent is gone (LD08).</summary>
        [JsonPropertyName("radius")]
        [JsonConverter(typeof(NullableDoubleConverter))]
        public double? Radius { get; set; }
    }

    /// <summary>
    /// The body of <c>PUT api/landmarks/{id}</c>, as the form and <c>/knk landmark</c> send it. The route id wins over a
    /// body id. <c>name</c> is required and the parent is taken as sent (<c>parentDomainId</c>, else <c>domainId</c>;
    /// null unlinks). The pin, the area (<c>areaKind</c> with <c>wgRegionId</c>), the description, the access, message and
    /// warp settings, <c>preference</c>, <c>navigationEnabled</c> and <c>kind</c> keep their stored values when null.
    /// </summary>
    public class LandmarkUpdateDto : LandmarkCreateDto
    {
        [JsonPropertyName("id")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? Id { get; set; }
    }
}
