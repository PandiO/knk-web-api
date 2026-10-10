using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// KNG-111: what a domain form knows about the domain's world, sent before submitting so the form can ask for the
    /// world when nothing else gives it. Mirrors the create/update payload fields that carry a world.
    /// </summary>
    public class DomainWorldResolveRequestDto
    {
        /// <summary>Town, District, Structure, GateStructure or Domain.</summary>
        [JsonPropertyName("entityType")]
        public string? EntityType { get; set; }

        /// <summary>The domain being edited; null when creating.</summary>
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("worldName")]
        public string? WorldName { get; set; }

        [JsonPropertyName("wgRegionId")]
        public string? WgRegionId { get; set; }

        [JsonPropertyName("locationId")]
        public int? LocationId { get; set; }

        [JsonPropertyName("locationWorld")]
        public string? LocationWorld { get; set; }

        /// <summary>A District's parent.</summary>
        [JsonPropertyName("townId")]
        public int? TownId { get; set; }

        /// <summary>A Structure's or GateStructure's parent.</summary>
        [JsonPropertyName("districtId")]
        public int? DistrictId { get; set; }
    }

    public class DomainWorldResolutionDto
    {
        /// <summary>The resolved world, or null.</summary>
        [JsonPropertyName("worldName")]
        public string? WorldName { get; set; }

        /// <summary>Where the world came from: form, region world task, location, parent domain or saved domain.</summary>
        [JsonPropertyName("source")]
        public string? Source { get; set; }

        /// <summary>True when no source gives a world and nothing conflicts: the form must ask for it.</summary>
        [JsonPropertyName("needsWorld")]
        public bool NeedsWorld { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("errorCode")]
        public string? ErrorCode { get; set; }
    }

    /// <summary>
    /// KNG-111: the game server's report of which loaded world(s) contain each WorldGuard region id, used to fill
    /// <c>WorldName</c> on domains created before worlds were stored.
    /// </summary>
    public class DomainWorldBackfillRequestDto
    {
        [JsonPropertyName("regions")]
        public List<DomainRegionWorldsDto> Regions { get; set; } = new();
    }

    public class DomainRegionWorldsDto
    {
        [JsonPropertyName("wgRegionId")]
        public string WgRegionId { get; set; } = null!;

        [JsonPropertyName("worlds")]
        public List<string> Worlds { get; set; } = new();
    }

    public class DomainWorldBackfillResultDto
    {
        /// <summary>Domains given a world by this pass.</summary>
        [JsonPropertyName("updated")]
        public int Updated { get; set; }

        /// <summary>Domains still without a world: an admin has to choose it in the domain's form.</summary>
        [JsonPropertyName("unresolved")]
        public List<DomainWorldMissingDto> Unresolved { get; set; } = new();
    }

    public class DomainWorldMissingDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("domainType")]
        public string DomainType { get; set; } = null!;

        [JsonPropertyName("wgRegionId")]
        public string? WgRegionId { get; set; }

        /// <summary>The worlds the game server found the region in (empty: not found; several: ambiguous).</summary>
        [JsonPropertyName("candidateWorlds")]
        public List<string> CandidateWorlds { get; set; } = new();
    }
}
