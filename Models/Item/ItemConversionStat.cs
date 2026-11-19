namespace knkwebapi_v2.Models;

/// <summary>
/// Item identity (PandiO/knk-workspace#141, production DESIGN §8): how many obtained items the game server resolved,
/// normalised or could not resolve, summed per UTC day, conversion hook, outcome and material. Written only by the
/// plugin (<c>POST api/ItemIdentity/conversion-counts</c>); read by the admin report
/// (<c>GET api/ItemIdentity/report</c>). An aggregate counter table, not a record people open: no form or dashboard
/// type (agent decision on #141, like <c>MinecraftCatalogScan</c>).
/// </summary>
public class ItemConversionStat
{
    public int Id { get; set; }

    /// <summary>The UTC day (time 00:00) the counts belong to.</summary>
    public DateTime Day { get; set; }

    /// <summary>The hook: <c>pickup</c>, <c>block_drop</c>, <c>entity_death</c>, <c>fishing</c>, <c>craft</c>,
    /// <c>trade</c> or <c>container_open</c>.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary><c>vanilla_match</c>, <c>blueprint_tag</c>, <c>instance_tag</c>, <c>normalized</c> or
    /// <c>unresolved_*</c> (the reason).</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>The item's material, e.g. <c>minecraft:oak_log</c>.</summary>
    public string MaterialKey { get; set; } = string.Empty;

    public long Count { get; set; }
}
