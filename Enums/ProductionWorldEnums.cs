namespace knkwebapi_v2.Enums
{
    /// <summary>Where a production structure sends its output (production DESIGN § 3.3, § 7.4).</summary>
    public enum WarehousePreference
    {
        /// <summary>The nearest warehouse of the structure's town that accepts deliveries.</summary>
        Nearest,
        /// <summary>The structure's <c>PreferredWarehouseId</c> (falls back to Nearest when it can't take it).</summary>
        Preferred,
    }

    /// <summary>A production structure's link to its town's road network (production DESIGN § 7.6, D5).</summary>
    public enum RoadConnectionState
    {
        Unmeasured,
        Connected,
        Unconnected,
    }
}
