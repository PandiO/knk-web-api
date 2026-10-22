using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>One server scan of the Minecraft registry (KNG-200 §2.3, used from phase 3).</summary>
public class MinecraftCatalogScan
{
    public int Id { get; set; }

    public MinecraftCatalogScanStatus Status { get; set; } = MinecraftCatalogScanStatus.Requested;

    public int? RequestedByUserId { get; set; }

    public int? WorldTaskId { get; set; }

    public string? MinecraftVersion { get; set; }

    public string? ServerVersion { get; set; }

    public int? SchemaVersion { get; set; }

    public string? ContentHash { get; set; }

    public int? EntryCount { get; set; }

    public int Inserted { get; set; }

    public int Updated { get; set; }

    public int Unchanged { get; set; }

    public int Merged { get; set; }

    public int Deleted { get; set; }

    public int MarkedMissing { get; set; }

    public int IconsMissing { get; set; }

    // KNG-250: the enchantment part of a schema 2 snapshot (null/0 for schema 1).
    public int? EnchantmentCount { get; set; }

    public int EnchantmentsInserted { get; set; }

    public int EnchantmentsUpdated { get; set; }

    public int EnchantmentsUnchanged { get; set; }

    public int EnchantmentsMerged { get; set; }

    public int EnchantmentsDeleted { get; set; }

    public int EnchantmentsMarkedMissing { get; set; }

    public int EnchantmentDefinitionsCreated { get; set; }

    public int EnchantmentDefinitionsLinked { get; set; }

    public string? Error { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }
}
