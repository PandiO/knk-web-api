using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace knkwebapi_v2.Dtos
{
    public class StreetDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("districtIds")]
        public List<int> DistrictIds { get; set; } = new();

        // Optional embedded Districts collection
        [JsonPropertyName("districts")]
        public IEnumerable<StreetDistrictDto>? Districts { get; set; }

        // Optional embedded Structures collection
        [JsonPropertyName("structures")]
        public IEnumerable<StreetStructureDto>? Structures { get; set; }

        // Road navigation (docs/specs/navigation/DESIGN.md §3.7): read-only counts of the road
        // edges labelled with this street, filled by StreetService.
        [JsonPropertyName("edgeCount")]
        public int EdgeCount { get; set; }

        [JsonPropertyName("totalLength")]
        public double TotalLength { get; set; }
    }

    public class StreetListDto
    {
        [JsonPropertyName("id")]
        public int? id { get; set; }

        [JsonPropertyName("name")]
        public string name { get; set; } = null!;
    }
}

namespace knkwebapi_v2.Dtos
{
    // Lightweight District DTO for embedding in Street payloads
    public class StreetDistrictDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("town")]
        public StreetDistrictTownDto? Town { get; set; }

        [JsonPropertyName("allowEntry")]
        public bool? AllowEntry { get; set; }

        [JsonPropertyName("allowExit")]
        public bool? AllowExit { get; set; }

        [JsonPropertyName("wgRegionId")]
        public string? WgRegionId { get; set; }
    }

    // Lightweight Structure DTO for embedding in Street payloads
    public class StreetStructureDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("houseNumber")]
        public int? HouseNumber { get; set; }

        [JsonPropertyName("districtId")]
        public int? DistrictId { get; set; }
    }

    public class StreetDistrictTownDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
