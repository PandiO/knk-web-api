namespace knkwebapi_v2.Enums
{
    // Town logistics P9 (PandiO/knk-workspace#127, TOWN_LOGISTICS.md § 2-5). Stored as strings, so the order of the
    // members is free to change; new members are additive (the Later rule types of #324 are added with that issue).

    /// <summary>What a logistics policy belongs to (TOWN_LOGISTICS § 4, § 4.1).</summary>
    public enum LogisticsPolicyScope
    {
        /// <summary>A town's policy (one current version per town).</summary>
        Town,
        /// <summary>An optional per-warehouse policy (one current version per warehouse); its rules apply to that warehouse.</summary>
        Warehouse,
        /// <summary>A default template per town size and specialization (D-L3), edited by admins and copied into a town.</summary>
        Template,
    }

    /// <summary>A template's town size label (TOWN_LOGISTICS § 4.1: "per town size and specialization").</summary>
    public enum LogisticsTownSize
    {
        Village,
        Town,
        City,
    }

    /// <summary>The V1 rule types of the catalogue (TOWN_LOGISTICS § 4). Ceiling, allocation quota, rebalance,
    /// export/import, emergency mode and tax are [Later] (#324).</summary>
    public enum LogisticsRuleType
    {
        /// <summary>Stock that may not be reserved for consumers below this level: units, or days of cover; town-wide or
        /// local to one warehouse.</summary>
        Reserve,
        /// <summary>Under scarcity, higher classes are served first (per consumer or group).</summary>
        PriorityClass,
        /// <summary>Which warehouse serves a district, the town's rural territory or one structure; also the inbound
        /// preference of producers there.</summary>
        ServiceArea,
        /// <summary>Default reorder point and target level for a consumer group (the per-storage settings win).</summary>
        Replenishment,
    }

    /// <summary>Where a rule applies (TOWN_LOGISTICS § 4: "scope: town or warehouse").</summary>
    public enum LogisticsRuleScope
    {
        Town,
        Warehouse,
    }

    /// <summary>Which items a rule is about (TOWN_LOGISTICS § 4: "target: blueprint, category or tag").</summary>
    public enum LogisticsTargetKind
    {
        Any,
        Blueprint,
        Category,
        Tag,
    }

    /// <summary>Which consumers a priority or replenishment rule is about ("per consumer or group").</summary>
    public enum LogisticsConsumerKind
    {
        Any,
        /// <summary>One structure.</summary>
        Structure,
        /// <summary>Every production structure of one kind (bakeries, smithies, ...).</summary>
        ProductionKind,
    }

    /// <summary>Which places a service area rule maps to its warehouse (TOWN_LOGISTICS § 2).</summary>
    public enum LogisticsAreaKind
    {
        /// <summary>A district of the town.</summary>
        District,
        /// <summary>The town's rural territory (rural structures with this town).</summary>
        Rural,
        /// <summary>One structure (a producer or a consumer).</summary>
        Structure,
    }

    /// <summary>Under scarcity, higher classes are served first (TOWN_LOGISTICS § 4). Ranked by <c>Rank()</c>.</summary>
    public enum LogisticsPriorityClass
    {
        Critical,
        High,
        Normal,
        Low,
    }

    /// <summary>Who wrote a policy version or rule (TOWN_LOGISTICS § 4: "the author: admin, or later the clan council").</summary>
    public enum LogisticsAuthor
    {
        Admin,
        /// <summary>[Later] the ruling clan's council (§ 4.1).</summary>
        ClanCouncil,
    }

    /// <summary>A planner reservation's state (TOWN_LOGISTICS § 5.1).</summary>
    public enum LogisticsReservationStatus
    {
        /// <summary>Holds warehouse stock for the consumer until its worker collects it or it expires.</summary>
        Open,
        /// <summary>The consumer's worker loaded the goods (the collection order left the warehouse).</summary>
        Collected,
        /// <summary>Not collected before its expiry, or its collection order ended without loading: the stock is free again.</summary>
        Released,
        /// <summary>Released by staff.</summary>
        Cancelled,
    }

    /// <summary>Why the planner ran.</summary>
    public enum LogisticsRunTrigger
    {
        /// <summary>The 10-minute schedule (D-L2).</summary>
        Scheduled,
        /// <summary>Staff or the game server asked for a run now.</summary>
        Manual,
    }

    /// <summary>The planner's answer for one consumer storage and blueprint (TOWN_LOGISTICS § 5 step 6: "why did (or
    /// didn't) the bakery get flour").</summary>
    public enum LogisticsPlanOutcome
    {
        /// <summary>Reserved up to the target level.</summary>
        Reserved,
        /// <summary>Reserved less than the target level (scarcity, a reserve, the worker's capacity or the consumer's room).</summary>
        PartlyReserved,
        /// <summary>Stock (with what is on the way) is above the reorder point.</summary>
        NotNeeded,
        /// <summary>An open reservation for it is waiting to be collected.</summary>
        AlreadyReserved,
        /// <summary>No warehouse of the town holds it.</summary>
        NoStock,
        /// <summary>There is stock, but the reserves keep it (or higher classes took what was free).</summary>
        HeldByReserve,
        /// <summary>The town has no warehouse.</summary>
        NoWarehouse,
        /// <summary>The consumer has no worker of its own to collect with.</summary>
        NoWorker,
        /// <summary>The consumer storage has no room.</summary>
        NoRoom,
        /// <summary>A rural consumer without a road connection (D5): no off-road collection.</summary>
        NotConnected,
        /// <summary>The worker's capacity was used by the consumer's other lines this run.</summary>
        CarrierFull,
    }
}
