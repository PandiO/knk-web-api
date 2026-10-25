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

        /// <summary>The icon shown for this material (override first).</summary>
        [JsonPropertyName("iconUrl")]
        public string? IconUrl { get; set; }
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

    /// <summary>An enchantment the latest scan didn't see that definitions still use (KNG-250).</summary>
    public class MinecraftCatalogAuditEnchantmentDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("namespaceKey")]
        public string NamespaceKey { get; set; } = null!;

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("isCustom")]
        public bool IsCustom { get; set; }

        [JsonPropertyName("catalogStatus")]
        public string CatalogStatus { get; set; } = null!;

        [JsonPropertyName("definitions")]
        public List<MinecraftCatalogAuditDefinitionDto> Definitions { get; set; } = new();
    }

    /// <summary>An EnchantmentDefinition in the audit.</summary>
    public class MinecraftCatalogAuditDefinitionDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("key")]
        public string Key { get; set; } = null!;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = null!;
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

        /// <summary>Active materials the scan found no icon for and that have no override.</summary>
        [JsonPropertyName("missingIcons")]
        public List<MinecraftCatalogAuditMaterialDto> MissingIcons { get; set; } = new();

        /// <summary>Materials with a manual icon, so the admin page can change or clear it.</summary>
        [JsonPropertyName("iconOverrides")]
        public List<MinecraftCatalogAuditMaterialDto> IconOverrides { get; set; } = new();

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

        [JsonPropertyName("enchantmentCount")]
        public int EnchantmentCount { get; set; }

        [JsonPropertyName("unverifiedEnchantmentCount")]
        public int UnverifiedEnchantmentCount { get; set; }

        /// <summary>Enchantments not in the latest scan that definitions still use (KNG-250).</summary>
        [JsonPropertyName("missingEnchantmentsReferenced")]
        public List<MinecraftCatalogAuditEnchantmentDto> MissingEnchantmentsReferenced { get; set; } = new();
    }

    /// <summary>A scan as the admin page and the plugin see it (KNG-200 plan §5.2a).</summary>
    public class MinecraftCatalogScanDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = null!;

        [JsonPropertyName("requestedByUserId")]
        public int? RequestedByUserId { get; set; }

        [JsonPropertyName("worldTaskId")]
        public int? WorldTaskId { get; set; }

        [JsonPropertyName("minecraftVersion")]
        public string? MinecraftVersion { get; set; }

        [JsonPropertyName("serverVersion")]
        public string? ServerVersion { get; set; }

        [JsonPropertyName("entryCount")]
        public int? EntryCount { get; set; }

        [JsonPropertyName("inserted")]
        public int Inserted { get; set; }

        [JsonPropertyName("updated")]
        public int Updated { get; set; }

        [JsonPropertyName("unchanged")]
        public int Unchanged { get; set; }

        [JsonPropertyName("merged")]
        public int Merged { get; set; }

        [JsonPropertyName("deleted")]
        public int Deleted { get; set; }

        [JsonPropertyName("markedMissing")]
        public int MarkedMissing { get; set; }

        [JsonPropertyName("iconsMissing")]
        public int IconsMissing { get; set; }

        /// <summary>Enchantments in the snapshot (KNG-250); null for a schema 1 snapshot.</summary>
        [JsonPropertyName("enchantmentCount")]
        public int? EnchantmentCount { get; set; }

        [JsonPropertyName("enchantmentsInserted")]
        public int EnchantmentsInserted { get; set; }

        [JsonPropertyName("enchantmentsUpdated")]
        public int EnchantmentsUpdated { get; set; }

        [JsonPropertyName("enchantmentsUnchanged")]
        public int EnchantmentsUnchanged { get; set; }

        [JsonPropertyName("enchantmentsMerged")]
        public int EnchantmentsMerged { get; set; }

        [JsonPropertyName("enchantmentsDeleted")]
        public int EnchantmentsDeleted { get; set; }

        [JsonPropertyName("enchantmentsMarkedMissing")]
        public int EnchantmentsMarkedMissing { get; set; }

        [JsonPropertyName("enchantmentDefinitionsCreated")]
        public int EnchantmentDefinitionsCreated { get; set; }

        [JsonPropertyName("enchantmentDefinitionsLinked")]
        public int EnchantmentDefinitionsLinked { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("completedAt")]
        public DateTime? CompletedAt { get; set; }
    }

    /// <summary>The snapshot knk-plugin sends with <c>PUT api/MinecraftCatalog/scans/{id}/snapshot</c> (schema version 1 or 2).</summary>
    public class MinecraftCatalogSnapshotDto
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("minecraftVersion")]
        public string? MinecraftVersion { get; set; }

        [JsonPropertyName("serverVersion")]
        public string? ServerVersion { get; set; }

        [JsonPropertyName("contentHash")]
        public string? ContentHash { get; set; }

        [JsonPropertyName("entries")]
        public List<MinecraftCatalogEntryDto>? Entries { get; set; }

        /// <summary>Schema 2 (KNG-250): vanilla and custom enchantments.</summary>
        [JsonPropertyName("enchantments")]
        public List<MinecraftCatalogEnchantmentDto>? Enchantments { get; set; }
    }

    /// <summary>One enchantment of a schema 2 snapshot: the server's registry or knk-plugin's custom ones (KNG-250 §12.2).</summary>
    public class MinecraftCatalogEnchantmentDto
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = null!;

        [JsonPropertyName("isCustom")]
        public bool IsCustom { get; set; }

        [JsonPropertyName("maxLevel")]
        public int? MaxLevel { get; set; }

        [JsonPropertyName("translationKey")]
        public string? TranslationKey { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        /// <summary>ATTACK/SUPPORT for custom enchantments; null for vanilla ones.</summary>
        [JsonPropertyName("category")]
        public string? Category { get; set; }
    }

    /// <summary>One registry entry of a snapshot.</summary>
    public class MinecraftCatalogEntryDto
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = null!;

        [JsonPropertyName("isItem")]
        public bool IsItem { get; set; }

        [JsonPropertyName("isBlock")]
        public bool IsBlock { get; set; }

        [JsonPropertyName("maxStackSize")]
        public int? MaxStackSize { get; set; }

        [JsonPropertyName("translationKey")]
        public string? TranslationKey { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("defaultBlockState")]
        public string? DefaultBlockState { get; set; }
    }

    /// <summary>Body of <c>PUT api/MinecraftCatalog/materials/{id}/icon-override</c>; null clears the override.</summary>
    public class MinecraftIconOverrideDto
    {
        [JsonPropertyName("iconUrlOverride")]
        public string? IconUrlOverride { get; set; }
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
