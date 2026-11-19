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
}
