using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    public class DomainDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;
        [JsonPropertyName("description")]
        public string Description { get; set; } = null!;
        [JsonPropertyName("createdAt")]
        public DateTime? CreatedAt { get; set; }
        [JsonPropertyName("allowEntry")]
        public bool? AllowEntry { get; set; }
        [JsonPropertyName("allowExit")]
        public bool? AllowExit { get; set; }
        [JsonPropertyName("wgRegionId")]
        public string WgRegionId { get; set; } = null!;
        [JsonPropertyName("locationId")]
        public int? LocationId { get; set; }
        [JsonPropertyName("parentDomainId")]
        public int? ParentDomainId { get; set; }
        [JsonPropertyName("parentDomain")]
        public ParentDomainDto? ParentDomain { get; set; }
    }

    public class ParentDomainDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;
        [JsonPropertyName("parentDomainId")]
        public int? ParentDomainId { get; set; }
        [JsonPropertyName("parentDomain")]
        public ParentDomainDto? ParentDomain { get; set; }
        public string domainSubType { get; set; } = null!;
    }

    public class DomainListDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;
        [JsonPropertyName("description")]
        public string Description { get; set; } = null!;
        [JsonPropertyName("wgRegionId")]
        public string WgRegionId { get; set; } = null!;
        [JsonPropertyName("parentDomainId")]
        public int? ParentDomainId { get; set; }
        [JsonPropertyName("parentDomain")]
        public ParentDomainDto? ParentDomain { get; set; }
        // Concrete Domain subtype (Town/District/Structure/...), same convention as
        // DomainRegionDecisionDto.DomainType - lets a picker show "Ironhaven (Town)" instead of a bare name
        // (docs/specs/items/IMPLEMENTATION_PLAN.md §3.2/open question 4).
        [JsonPropertyName("domainType")]
        public string DomainType { get; set; } = null!;
    }

    public class DomainRegionDecisionDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;
        [JsonPropertyName("description")]
        public string Description { get; set; } = null!;
        [JsonPropertyName("wgRegionId")]
        public string WgRegionId { get; set; } = null!;
        [JsonPropertyName("allowEntry")]
        public bool AllowEntry { get; set; }
        [JsonPropertyName("allowExit")]
        public bool AllowExit { get; set; }
        [JsonPropertyName("domainType")]
        public string DomainType { get; set; } = null!;
        [JsonPropertyName("parentDomainDecisions")]
        public Collection<DomainRegionDecisionDto> ParentDomainDecisions { get; set; } = new Collection<DomainRegionDecisionDto>();
        [JsonPropertyName("childDomainDecisions")]
        public Collection<DomainRegionDecisionDto> ChildDomainDecisions { get; set; } = new Collection<DomainRegionDecisionDto>();
    }

    public class DomainRegionQueryDto
    {
        public IEnumerable<String>? WgRegionIds { get; set; }
        /***
            * If true, the hierarchy is traversed from top to bottom (i.e., parent to child).
            * If false, the hierarchy is traversed from bottom to top (i.e., child to parent).
            * Example: If true, ordering is done with Town -> District -> Structure.
            * If false, ordering is done with Structure -> District -> Town.
            ***/
        public bool? TopDownHierarchy { get; set; } = true;
    }
}
