namespace knkwebapi_v2.Enums
{
    // Shops P10 (PandiO/knk-workspace#248, shops DESIGN § 2-5). Stored as strings; new members are additive.

    /// <summary>What a shop does with goods (shops DESIGN § 2.2). Every shop can also buy from players and resell.</summary>
    public enum ShopKind
    {
        /// <summary>Sells goods as they are (general store, market stall).</summary>
        Trading,
        /// <summary>Turns inputs into goods on production lines (Input → Merchandise), then sells them (bakery, blacksmith).</summary>
        Processing,
    }

    /// <summary>Where a bought item goes (P7 delivery): placed by the game server in the player's inventory, or delivered
    /// to the player's inbox (needs <c>Production:Enabled</c>).</summary>
    public enum ShopDeliveryTarget
    {
        Inventory,
        Inbox,
    }

    /// <summary>Where a sale's goods come from.</summary>
    public enum ShopStockMode
    {
        /// <summary>From the shop's Merchandise storage; sold out at zero.</summary>
        Stocked,
        /// <summary>The static admin catalogue fallback (shops DESIGN § 2.3): never sold out, written to the ledger as
        /// <c>SYS_ADJUSTMENT</c> → <c>SYS_WORLD</c>. Blueprint entries only.</summary>
        Unlimited,
    }

    /// <summary>Which way a display spot's item faces (display stalls P12, PandiO/knk-workspace#250): towards the player
    /// standing in front of it. In game it is taken from the admin's facing at registration (they look at the block).</summary>
    public enum ShopDisplayFacing
    {
        North,
        East,
        South,
        West,
    }
}
