namespace knkwebapi_v2.Enums
{
    // Stock core P1 (PandiO/knk-workspace#97, production DESIGN § 4). Stored as strings, so the order of the
    // members is free to change; new members are additive.

    /// <summary>What holds a storage (DESIGN § 4.1): exactly one holder per kind, enforced by a check constraint.</summary>
    public enum StorageKind
    {
        /// <summary>A domain (a structure, town or district): <c>DomainId</c>.</summary>
        Domain,
        /// <summary>A player: <c>UserId</c> (inbox, stash, vault).</summary>
        Player,
        /// <summary>A clan: <c>ClanId</c>.</summary>
        Clan,
        /// <summary>The cargo of one transport order while it travels (P5); no holder column.</summary>
        Transit,
        /// <summary>A system storage (<c>SystemKey</c>): the other side of stock entering or leaving the system.</summary>
        Virtual,
    }

    /// <summary>What a storage is for (DESIGN § 4.1).</summary>
    public enum StoragePurpose
    {
        General,
        Output,
        Input,
        Delivery,
        Merchandise,
        Inbox,
        Stash,
        Vault,
    }

    /// <summary>Why stock moved (DESIGN § 4.3, initial list). One reason per transaction.</summary>
    public enum StockReason
    {
        Production,
        ProductionInput,
        TransportLoad,
        TransportDeliver,
        TransportReturn,
        TransportRobbed,
        TransportLost,
        Distribution,
        Trade,
        Deposit,
        Withdraw,
        RewardInbox,
        InboxClaim,
        InboxOverflow,
        InboxExpired,
        ContainerSync,
        PlayerHarvest,
        Spoilage,
        AdminAdjust,
        /// <summary>Resources paid for a production structure's level upgrade (PRODUCTION_KINDS § 5), to <c>SYS_CONSUMPTION</c>.</summary>
        Upgrade,
        /// <summary>A player bought from a shop (P10): Merchandise (or <c>SYS_ADJUSTMENT</c> for an unlimited catalogue
        /// entry) → <c>SYS_WORLD</c>, with the coins to the town account in the same database transaction.</summary>
        ShopSale,
        /// <summary>A player sold to a shop (P10): <c>SYS_WORLD</c> → Merchandise, paid the buy-back price from the town
        /// account in the same database transaction.</summary>
        ShopBuyBack,
        /// <summary>A compensating transaction that undoes another one (corrections are new moves, never edits).</summary>
        Correction,
    }

    /// <summary>How a move treats a line that doesn't fully fit (DESIGN § 4.5).</summary>
    public enum StockMoveMode
    {
        /// <summary>Every line moves in full, or nothing moves.</summary>
        AllOrNothing,
        /// <summary>Each line moves what fits; the rest stays in the source and is reported as rejected.</summary>
        Partial,
    }

    /// <summary>Why (part of) a line was not moved. Capacity is a result, never an exception (V2 defects 1, 2, 4).</summary>
    public enum StockRejectReason
    {
        None,
        CapacityFull,
        DistinctCapReached,
        NotEnoughStock,
        Locked,
        /// <summary>Another line of the same all-or-nothing request was refused, so this one was not applied.</summary>
        OtherLineRejected,
    }

    /// <summary>The outcome of a stock request.</summary>
    public enum StockResultStatus
    {
        /// <summary>Every line moved in full.</summary>
        Applied,
        /// <summary>Some lines (or parts of lines) moved; the rest is reported as rejected.</summary>
        PartiallyApplied,
        /// <summary>Nothing moved; no transaction was written.</summary>
        Rejected,
    }

    /// <summary>Who asked for a stock transaction.</summary>
    public enum StockActorKind
    {
        User,
        System,
        PluginServer,
    }

    // P6a (PandiO/knk-workspace#683, DESIGN § 6.2): containers bound to a storage. Stored as strings.

    /// <summary>How a bound block shows its storage. Backed mode (a normal chest mirrored into the API) is Later.</summary>
    public enum StorageContainerMode
    {
        /// <summary>Opening the block shows the storage as a paged menu; take and put are ledger moves.</summary>
        Terminal,
        /// <summary>The block is filled read-only with a sample of the stock (fill level visible).</summary>
        Display,
    }

    /// <summary>The block types a storage can be bound to (DESIGN § 6.3).</summary>
    public enum StorageContainerType
    {
        Chest,
        TrappedChest,
        Barrel,
    }
}

namespace knkwebapi_v2.Enums
{
    // P7 (PandiO/knk-workspace#60, DESIGN § 4.6): item delivery to players. Stored as strings.

    /// <summary>Why a player gets items (the entry points of DESIGN § 4.6).</summary>
    public enum ItemDeliveryReason
    {
        Kit,
        Lootbox,
        Quest,
        Gift,
        Admin,
        Other,
        /// <summary>A shop purchase delivered to the inbox (shops P10, #248).</summary>
        ShopPurchase,
    }

    /// <summary>What a delivery did when it arrived: everything in the inbox, part lost at the cap, or all lost.</summary>
    public enum ItemDeliveryStatus
    {
        Inboxed,
        PartlyLost,
        Lost,
    }
}
