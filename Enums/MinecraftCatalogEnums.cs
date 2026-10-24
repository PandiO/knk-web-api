namespace knkwebapi_v2.Enums;

// Minecraft catalogue enums (knk-workspace docs/specs/minecraft-catalog/IMPLEMENTATION_PLAN.md §2, KNG-200).
// All stored as their string names.

/// <summary>Where a <see cref="knkwebapi_v2.Models.MinecraftMaterialRef"/> stands against the server scans.</summary>
public enum MinecraftCatalogStatus
{
    /// <summary>Created by a seed, a form or the backfill and never seen in a scan.</summary>
    Unverified = 0,
    /// <summary>In the latest completed scan.</summary>
    Active = 1,
    /// <summary>Not in the latest scan, kept because something still references it.</summary>
    Missing = 2
}

/// <summary>Result of resolving a material's icon (phase 3).</summary>
public enum MinecraftIconStatus
{
    Resolved = 0,
    Missing = 1,
    Broken = 2
}

/// <summary>Lifecycle of a <see cref="knkwebapi_v2.Models.MinecraftCatalogScan"/>.</summary>
public enum MinecraftCatalogScanStatus
{
    Requested = 0,
    Running = 1,
    Ingesting = 2,
    Completed = 3,
    Failed = 4
}
