namespace knkwebapi_v2.Enums
{
    // Transport core P5 (PandiO/knk-workspace#79, production DESIGN § 7). Stored as strings, so the order of the members
    // is free to change; new members are additive.

    /// <summary>Why a transport order exists (DESIGN § 7.1).</summary>
    public enum TransportOrigin
    {
        /// <summary>Planned by the sweep for a producer whose output reached a trigger (§ 7.4).</summary>
        Automatic,
        /// <summary>A consumer's collection order (TOWN_LOGISTICS § 5, P9).</summary>
        Distribution,
        /// <summary>[Later] Under a trade agreement.</summary>
        Trade,
        /// <summary>Created by staff.</summary>
        Manual,
        /// <summary>Created by a quest script.</summary>
        Quest,
    }

    /// <summary>
    /// The lifecycle shared by every carrier (DESIGN § 7.2). V1 (simulated carriers, robbery off, D6) uses Planned,
    /// Assigned, Loading, InTransit, AtStop, AwaitingSpace, Returning, ReturnBlocked, Delivered, Returned and Cancelled;
    /// Ambushed, Defended, Robbed, Lost and Stalled are reserved for physical carriers (#53, #251).
    /// </summary>
    public enum TransportStatus
    {
        Planned,
        Assigned,
        Loading,
        InTransit,
        AtStop,
        /// <summary>A stop had no room for (all of) its planned amounts: the carrier waits, then re-routes or returns.</summary>
        AwaitingSpace,
        /// <summary>The cargo goes back to the source storage (no room anywhere, or cancelled after loading).</summary>
        Returning,
        /// <summary>Back at the source, which has no room: the carrier waits there until it has (cargo is never dropped).</summary>
        ReturnBlocked,
        Delivered,
        Returned,
        Cancelled,
        Ambushed,
        Defended,
        Robbed,
        Lost,
        Stalled,
    }

    /// <summary>How the cargo travels (DESIGN § 7.3).</summary>
    public enum TransportCarrierMode
    {
        /// <summary>Nothing walks: the trip takes route length ÷ speed (G14). [V1]</summary>
        Simulated,
        /// <summary>[Later] #251.</summary>
        NpcCaravan,
        /// <summary>[Later] A player with a sealed cargo crate.</summary>
        PlayerCourier,
    }

    /// <summary>The state of one stop of an order.</summary>
    public enum TransportStopStatus
    {
        Pending,
        Delivered,
        /// <summary>Part of the planned amounts was delivered; the rest went on to another stop or back.</summary>
        Partial,
        /// <summary>Nothing was delivered here (no room, re-routed or cancelled before arrival).</summary>
        Skipped,
    }

    /// <summary>One entry in an order's event log (DESIGN § 7.1).</summary>
    public enum TransportEventKind
    {
        Created,
        WaitingForWorker,
        Assigned,
        Loaded,
        Departed,
        ArrivedAtStop,
        Delivered,
        AwaitingSpace,
        Rerouted,
        ReturnStarted,
        Returned,
        ReturnBlocked,
        WorkerReturned,
        Completed,
        CancelRequested,
        Cancelled,
        Note,
    }

    /// <summary>Who measured a lane (DESIGN § 7.6).</summary>
    public enum TransportLaneSource
    {
        /// <summary>The game server's lane measurer (<c>AStarRouter</c> over the road snapshot, P5b #681).</summary>
        Plugin,
        /// <summary>Typed in by staff.</summary>
        Manual,
    }

    /// <summary>Where a leg's distance came from.</summary>
    public enum TransportDistanceSource
    {
        /// <summary>A measured, reachable lane: road length.</summary>
        Lane,
        /// <summary>No lane: straight distance between the two structures × the settings' factor.</summary>
        Estimated,
        /// <summary>No lane and no positions: the settings' fallback distance.</summary>
        Fallback,
        /// <summary>A carrier turned around on its leg: the way back is the way it came.</summary>
        TurnedAround,
    }
}
