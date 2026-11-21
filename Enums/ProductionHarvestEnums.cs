namespace knkwebapi_v2.Enums
{
    // Player harvesting P8a (PandiO/knk-workspace#685, PRODUCTION_KINDS.md § 6-7). Stored as strings, so new members are
    // additive and the order is free to change.

    /// <summary>What a harvest rule's targets name: block materials or entity types (PRODUCTION_KINDS § 6.1).</summary>
    public enum HarvestTargetKind
    {
        Block,
        Entity,
    }

    /// <summary>The tool a harvest rule asks for (§ 6.1 <c>Tool</c>); Any = no tool rule. The game server checks it.</summary>
    public enum HarvestToolClass
    {
        Any,
        Pickaxe,
        Axe,
        Shovel,
        Hoe,
        Shears,
        Sword,
        FishingRod,
    }

    /// <summary>The lowest tool tier a harvest rule accepts (§ 6.1 <c>Tool</c>); Any = every tier.</summary>
    public enum HarvestToolTier
    {
        Any,
        Wood,
        Stone,
        Iron,
        Gold,
        Diamond,
        Netherite,
    }

    /// <summary>
    /// What the game server leaves where a harvested block was until it regenerates (PRODUCTION_KINDS § 7, P-D7, P-D8):
    /// a neutral depleted block for ore and rock, a replanted crop at age 0, air for flowers, a sapling for a felled tree
    /// (restored by snapshot), or nothing (entities, population regulation).
    /// </summary>
    public enum HarvestPlaceholder
    {
        DepletedBlock,
        Replant,
        Air,
        Sapling,
        None,
    }

    /// <summary>How one entry of a harvest report ended.</summary>
    public enum PlayerHarvestStatus
    {
        /// <summary>Split and booked: the worker's share is paid, the town's share went to Output (or was lost, F10).</summary>
        Applied,

        /// <summary>The same idempotency key was booked before; the stored result is returned.</summary>
        Replayed,

        /// <summary>Not booked: the entry names a rule or area it may not use (the reason says why).</summary>
        Rejected,
    }
}
