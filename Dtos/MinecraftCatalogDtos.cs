using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>What the backfill changed (KNG-200 §8 step 2). A second run reports zeros.</summary>
    public class MinecraftCatalogBackfillResultDto
    {
        [JsonPropertyName("materialKeysNormalized")]
        public int MaterialKeysNormalized { get; set; }

        [JsonPropertyName("materialDisplayNamesFilled")]
        public int MaterialDisplayNamesFilled { get; set; }

        [JsonPropertyName("materialsCreated")]
        public int MaterialsCreated { get; set; }

        [JsonPropertyName("blockRefsCanonicalized")]
        public int BlockRefsCanonicalized { get; set; }

        [JsonPropertyName("blockRefsLinked")]
        public int BlockRefsLinked { get; set; }

        /// <summary>Rows the backfill left alone, with the reason (see the audit).</summary>
        [JsonPropertyName("skipped")]
        public List<string> Skipped { get; set; } = new();
    }

    /// <summary>A material row in the audit, with what references it.</summary>
    public class MinecraftCatalogAuditMaterialDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("namespaceKey")]
        public string NamespaceKey { get; set; } = null!;

        /// <summary>Null when the key can't be normalised.</summary>
        [JsonPropertyName("normalizedKey")]
        public string? NormalizedKey { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("catalogStatus")]
        public string CatalogStatus { get; set; } = null!;

        /// <summary>Count per referrer, e.g. <c>ItemBlueprint.IconMaterialRefId</c> → 3.</summary>
        [JsonPropertyName("references")]
        public Dictionary<string, int> References { get; set; } = new();

        [JsonPropertyName("totalReferences")]
        public int TotalReferences { get; set; }
    }

    /// <summary>A block ref row in the audit.</summary>
    public class MinecraftCatalogAuditBlockDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("namespaceKey")]
        public string NamespaceKey { get; set; } = null!;

        [JsonPropertyName("blockStateString")]
        public string? BlockStateString { get; set; }

        [JsonPropertyName("materialRefId")]
        public int? MaterialRefId { get; set; }

        [JsonPropertyName("stateKey")]
        public string? StateKey { get; set; }
    }

    /// <summary>Read-only report of what blocks the phase 4 constraints and what needs a merge (KNG-200 §8 step 3).</summary>
    public class MinecraftCatalogAuditDto
    {
        /// <summary>True when the phase 4 constraints (unique StateKey, required MaterialRefId) can be added.</summary>
        [JsonPropertyName("readyForConstraints")]
        public bool ReadyForConstraints { get; set; }

        [JsonPropertyName("materialCount")]
        public int MaterialCount { get; set; }

        [JsonPropertyName("unverifiedCount")]
        public int UnverifiedCount { get; set; }

        /// <summary>Groups of material rows whose keys normalise to the same key: merge each group into one row.</summary>
        [JsonPropertyName("materialKeyCollisions")]
        public List<List<MinecraftCatalogAuditMaterialDto>> MaterialKeyCollisions { get; set; } = new();

        /// <summary>Material keys that are not a valid namespaced key at all.</summary>
        [JsonPropertyName("invalidMaterialKeys")]
        public List<MinecraftCatalogAuditMaterialDto> InvalidMaterialKeys { get; set; } = new();

        /// <summary>Valid keys not stored in normal form (the backfill leaves them when the normal form is taken).</summary>
        [JsonPropertyName("notNormalizedMaterialKeys")]
        public List<MinecraftCatalogAuditMaterialDto> NotNormalizedMaterialKeys { get; set; } = new();

        /// <summary>Materials not in the latest scan that something still references (KNG-200 D3/D7).</summary>
        [JsonPropertyName("missingButReferenced")]
        public List<MinecraftCatalogAuditMaterialDto> MissingButReferenced { get; set; } = new();

        [JsonPropertyName("blockRefCount")]
        public int BlockRefCount { get; set; }

        /// <summary>Groups of block refs with the same identity: keep one, delete the others (nothing references block refs).</summary>
        [JsonPropertyName("duplicateBlockStates")]
        public List<List<MinecraftCatalogAuditBlockDto>> DuplicateBlockStates { get; set; } = new();

        [JsonPropertyName("invalidBlockStates")]
        public List<MinecraftCatalogAuditBlockDto> InvalidBlockStates { get; set; } = new();

        [JsonPropertyName("unlinkedBlockRefs")]
        public List<MinecraftCatalogAuditBlockDto> UnlinkedBlockRefs { get; set; } = new();

        /// <summary>Block refs whose stored key, state or StateKey differs from the canonical form or from their material.</summary>
        [JsonPropertyName("notCanonicalBlockRefs")]
        public List<MinecraftCatalogAuditBlockDto> NotCanonicalBlockRefs { get; set; } = new();
    }

    /// <summary>Result of merging one material into another.</summary>
    public class MinecraftMaterialMergeResultDto
    {
        [JsonPropertyName("sourceId")]
        public int SourceId { get; set; }

        [JsonPropertyName("targetId")]
        public int TargetId { get; set; }

        /// <summary>Rows repointed per referrer.</summary>
        [JsonPropertyName("repointed")]
        public Dictionary<string, int> Repointed { get; set; } = new();
    }
}
