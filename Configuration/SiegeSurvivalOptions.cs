namespace knkwebapi_v2.Configuration;

/// <summary>
/// Siege Survival runtime settings of the API (knk-workspace docs/specs/siege-survival/DESIGN.md §8a;
/// PandiO/knk-workspace#41, link S7a). Section "SiegeSurvival"; every value has a default, so the section is optional.
/// </summary>
public class SiegeSurvivalOptions
{
    public const string SectionName = "SiegeSurvival";

    /// <summary>
    /// How many entries a Mystery Box roll's reel carries (the in-world carousel of passing items, lootbox DESIGN
    /// §3.9), the last one being the result. At least 1.
    /// </summary>
    public int MysteryBoxReelLength { get; set; } = 20;
}
