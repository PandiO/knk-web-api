using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// A place that holds items (stock core P1, PandiO/knk-workspace#97, production DESIGN § 4.1). Every storage has a
/// <see cref="Kind"/> and at most one holder: <see cref="DomainId"/> (any domain: a structure, town or district),
/// <see cref="UserId"/>, <see cref="ClanId"/> or a <see cref="SystemKey"/> for the virtual storages; a transit storage
/// has none (its transport order points at it, P5). A check constraint (<c>CK_storages_Holder</c>) enforces it.
/// <para>
/// Quantities change only through <c>IStockService</c> (double-entry moves, § 4.3-4.5). <see cref="TotalUnits"/> and
/// <see cref="DistinctItems"/> are stored and kept in step by that service under the storage's row lock;
/// <see cref="RowVersion"/> is bumped on every change, so an admin edit and a move can't overwrite each other.
/// </para>
/// </summary>
[FormConfigurableEntity("Storage")]
public class Storage
{
    public const int NameMaxLength = 100;
    public const int SystemKeyMaxLength = 32;
    public const int LockReasonMaxLength = 200;

    // The virtual storages (DESIGN § 4.1), seeded by the migration with these ids.
    public const string SysProduction = "SYS_PRODUCTION";
    public const string SysConsumption = "SYS_CONSUMPTION";
    public const string SysLoss = "SYS_LOSS";
    public const string SysAdjustment = "SYS_ADJUSTMENT";
    public const string SysWorld = "SYS_WORLD";

    public static readonly IReadOnlyList<string> SystemKeys = new[] { SysProduction, SysConsumption, SysLoss, SysAdjustment, SysWorld };

    public int Id { get; set; }

    private string _name = null!;

    [MaxLength(NameMaxLength)]
    public string Name
    {
        get => _name;
        set => _name = value is null ? null! : value.Trim();
    }

    public StorageKind Kind { get; set; }

    public StoragePurpose Purpose { get; set; } = StoragePurpose.General;

    // ---- Holder: exactly the one column that matches Kind (CK_storages_Holder) ----

    [RelatedEntityField(typeof(Domain))]
    [NavigationPair(nameof(Domain))]
    public int? DomainId { get; set; }
    [RelatedEntityField(typeof(Domain))]
    public Domain? Domain { get; set; }

    [RelatedEntityField(typeof(User))]
    [NavigationPair(nameof(User))]
    public int? UserId { get; set; }
    [RelatedEntityField(typeof(User))]
    public User? User { get; set; }

    [RelatedEntityField(typeof(Clan))]
    [NavigationPair(nameof(Clan))]
    public int? ClanId { get; set; }
    [RelatedEntityField(typeof(Clan))]
    public Clan? Clan { get; set; }

    /// <summary>Set only on <see cref="StorageKind.Virtual"/> storages (<c>SYS_PRODUCTION</c>, …); unique.</summary>
    [MaxLength(SystemKeyMaxLength)]
    public string? SystemKey { get; set; }

    // ---- Capacity (DESIGN § 4.4 rev. 3), in raw units; capacity use = Σ amount × ItemBlueprint.StorageWeight ----

    /// <summary>Fixed raw cap; null = no cap. Virtual and transit storages are never capped.</summary>
    public long? MaxUnits { get; set; }

    /// <summary>At most this many distinct items (stock lines plus unique items); null = no cap.</summary>
    public int? MaxDistinct { get; set; }

    /// <summary>Raw units per bound container slot, for storages with bound containers (P6: cap = slots × this).
    /// Stored now; no container is bound in P1, so it does not change the cap yet.</summary>
    public int? UnitsPerSlot { get; set; }

    // ---- Stored totals, kept in step by IStockService ----

    /// <summary>Σ stock line amounts plus the number of unique items. Negative on virtual storages.</summary>
    public long TotalUnits { get; set; }

    /// <summary>Stock lines plus unique items held.</summary>
    public int DistinctItems { get; set; }

    /// <summary>Bumped on every change (moves and admin edits); a concurrency token.</summary>
    [ConcurrencyCheck]
    public long RowVersion { get; set; }

    // ---- Lock (DESIGN § 4.5): freezes the storage during sync conflicts, sieges or investigations ----

    /// <summary>While in the future, every move into or out of the storage is refused with <c>Locked</c>.</summary>
    public DateTime? LockedUntil { get; set; }

    [MaxLength(LockReasonMaxLength)]
    public string? LockReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [RelatedEntityField(typeof(StockLine))]
    public ICollection<StockLine> Lines { get; set; } = new List<StockLine>();

    [RelatedEntityField(typeof(StockUnit))]
    public ICollection<StockUnit> Units { get; set; } = new List<StockUnit>();

    public bool IsVirtual => Kind == StorageKind.Virtual;

    /// <summary>Capacity limits real storages only (DESIGN § 4.4).</summary>
    public bool IsCapped => Kind is not (StorageKind.Virtual or StorageKind.Transit);

    public bool IsLockedAt(DateTime nowUtc) => LockedUntil.HasValue && LockedUntil.Value > nowUtc;
}

/// <summary>A fungible stock line (DESIGN § 4.2): one blueprint in one storage. Unique per (storage, blueprint);
/// deleted when it reaches zero. Negative only in virtual storages. Listed on the storage's page (view only).</summary>
[FormConfigurableEntity("StockLine")]
public class StockLine
{
    public long Id { get; set; }

    public int StorageId { get; set; }
    public Storage Storage { get; set; } = null!;

    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    public long Amount { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A unique item in a storage (DESIGN § 4.2, serial tracking): an <see cref="ItemInstance"/> is in at most one
/// storage (unique index); with no row it is in the physical world (a player's hands) or not yet in the system.
/// Listed on the storage's page (view only).</summary>
[FormConfigurableEntity("StockUnit")]
public class StockUnit
{
    public long Id { get; set; }

    public int StorageId { get; set; }
    public Storage Storage { get; set; } = null!;

    public long ItemInstanceId { get; set; }
    public ItemInstance ItemInstance { get; set; } = null!;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
