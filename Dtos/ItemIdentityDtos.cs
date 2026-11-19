using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos;

// Item identity (PandiO/knk-workspace#141, production DESIGN §8).

/// <summary>One vanilla material and the one blueprint it resolves to.</summary>
public class ItemIdentityMapEntryDto
{
    [JsonPropertyName("materialKey")]
    public string MaterialKey { get; set; } = string.Empty;

    [JsonPropertyName("blueprintId")]
    public int BlueprintId { get; set; }

    /// <summary>True for a generated vanilla shadow (the blueprint is in the "Uncurated" category).</summary>
    [JsonPropertyName("shadow")]
    public bool Shadow { get; set; }
}

/// <summary>The material → blueprint map the game server caches (<c>GET api/ItemIdentity/material-map</c>).</summary>
public class ItemIdentityMapDto
{
    [JsonPropertyName("generatedAt")]
    public DateTime GeneratedAt { get; set; }

    [JsonPropertyName("entries")]
    public List<ItemIdentityMapEntryDto> Entries { get; set; } = new();
}

/// <summary>What a shadow generation run did (<c>POST api/ItemIdentity/shadows/generate</c>, and after every scan).</summary>
public class ItemShadowGenerationResultDto
{
    /// <summary>Catalogue materials that are items, active in the latest scan and not on the not-obtainable list.</summary>
    [JsonPropertyName("obtainableMaterials")]
    public int ObtainableMaterials { get; set; }

    [JsonPropertyName("alreadyMatched")]
    public int AlreadyMatched { get; set; }

    [JsonPropertyName("created")]
    public int Created { get; set; }

    [JsonPropertyName("uncuratedCategoryId")]
    public int UncuratedCategoryId { get; set; }

    /// <summary>True when the run had to create the "Uncurated" category.</summary>
    [JsonPropertyName("categoryCreated")]
    public bool CategoryCreated { get; set; }
}

/// <summary>Body of <c>PUT api/ItemIdentity/matches/{materialRefId}</c>: the blueprint that becomes the match, or null to clear it.</summary>
public class ItemIdentityMatchUpdateDto
{
    [JsonPropertyName("itemBlueprintId")]
    public int? ItemBlueprintId { get; set; }
}

/// <summary>Result of moving a vanilla match.</summary>
public class ItemIdentityMatchResultDto
{
    [JsonPropertyName("materialRefId")]
    public int MaterialRefId { get; set; }

    [JsonPropertyName("materialKey")]
    public string MaterialKey { get; set; } = string.Empty;

    [JsonPropertyName("previousBlueprintId")]
    public int? PreviousBlueprintId { get; set; }

    [JsonPropertyName("blueprintId")]
    public int? BlueprintId { get; set; }

    /// <summary>The material the new blueprint matched before, now unmatched (a blueprint holds one match).</summary>
    [JsonPropertyName("releasedMaterialRefId")]
    public int? ReleasedMaterialRefId { get; set; }
}

/// <summary>One count line from the game server.</summary>
public class ItemConversionCountDto
{
    [Required]
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("materialKey")]
    public string MaterialKey { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public long Count { get; set; }
}

/// <summary>Body of <c>POST api/ItemIdentity/conversion-counts</c>: counts since the last report, added to today's rows.</summary>
public class ItemConversionCountsDto
{
    /// <summary>The UTC day the counts belong to; null = today.</summary>
    [JsonPropertyName("day")]
    public DateTime? Day { get; set; }

    [JsonPropertyName("counts")]
    public List<ItemConversionCountDto> Counts { get; set; } = new();
}

/// <summary>An obtainable material without a blueprint.</summary>
public class ItemIdentityUnmatchedMaterialDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("namespaceKey")]
    public string NamespaceKey { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
}

/// <summary>Summed conversion counts.</summary>
public class ItemConversionTotalDto
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = string.Empty;

    [JsonPropertyName("materialKey")]
    public string? MaterialKey { get; set; }

    [JsonPropertyName("count")]
    public long Count { get; set; }
}

/// <summary>The admin read (<c>GET api/ItemIdentity/report</c>): coverage of the catalogue and conversion counts.</summary>
public class ItemIdentityReportDto
{
    [JsonPropertyName("obtainableMaterials")]
    public int ObtainableMaterials { get; set; }

    /// <summary>Obtainable materials with a blueprint (curated or shadow).</summary>
    [JsonPropertyName("matchedMaterials")]
    public int MatchedMaterials { get; set; }

    [JsonPropertyName("shadowMatches")]
    public int ShadowMatches { get; set; }

    [JsonPropertyName("curatedMatches")]
    public int CuratedMatches { get; set; }

    [JsonPropertyName("uncuratedCategoryId")]
    public int? UncuratedCategoryId { get; set; }

    [JsonPropertyName("unmatchedMaterials")]
    public List<ItemIdentityUnmatchedMaterialDto> UnmatchedMaterials { get; set; } = new();

    [JsonPropertyName("days")]
    public int Days { get; set; }

    /// <summary>Counts per source and outcome over <see cref="Days"/> days.</summary>
    [JsonPropertyName("conversions")]
    public List<ItemConversionTotalDto> Conversions { get; set; } = new();

    /// <summary>The materials most often left unresolved (custom-named, custom-lored, other plugins, no match).</summary>
    [JsonPropertyName("topUnresolved")]
    public List<ItemConversionTotalDto> TopUnresolved { get; set; } = new();
}
