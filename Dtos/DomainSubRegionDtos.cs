using System;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>A domain's sub-region (KNG-254): read and write shape of <c>api/DomainSubRegions</c>.</summary>
    public class DomainSubRegionDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("domainId")]
        public int DomainId { get; set; }

        /// <summary>The WorldGuard region: <c>tempregion_worldtask_&lt;n&gt;</c> from its world task, or an existing region
        /// adopted with "select"; <c>domain_&lt;domainId&gt;_sub_&lt;id&gt;</c> once saved.</summary>
        [JsonPropertyName("wgRegionId")]
        public string WgRegionId { get; set; } = null!;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime? CreatedAt { get; set; }
    }
}
