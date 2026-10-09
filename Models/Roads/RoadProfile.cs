using System;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// A named set of road materials with roles and widths, learned from survey walks or edited by
/// admins (docs/specs/navigation/DESIGN.md §3.1, §5.1). Edited through the road-profiles
/// endpoints, not FormWizard, so no [FormConfigurableEntity]. The plugin's ProfileLearner
/// recomputes a profile from <see cref="StatsJson"/> plus a new survey and PUTs the result
/// (plan D5); there is no server-side merge.
/// </summary>
public class RoadProfile
{
    public int Id { get; set; }

    /// <summary>Unique, max 100.</summary>
    public string Name { get; set; } = null!;

    public RoadClass RoadClass { get; set; } = RoadClass.Road;

    /// <summary>Per-profile tuning on top of the class cost.</summary>
    public double CostMultiplier { get; set; } = 1.0;

    /// <summary>JSON [{material, role, ambiguous, centreShare, edgeShare, samples}] (RoadMaterialDto).</summary>
    public string MaterialsJson { get; set; } = "[]";

    /// <summary>5th/95th percentile road width from surveys; plaza detection and leak limits.</summary>
    public int WidthMin { get; set; } = 1;
    public int WidthMax { get; set; } = 7;

    /// <summary>Total survey samples merged into this profile.</summary>
    public int SampleCount { get; set; }

    /// <summary>Disabled profiles are ignored by builds.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>JSON int[] of Town domain ids the profile applies inside, null = everywhere (plan D8).</summary>
    public string? ScopeTownIdsJson { get; set; }

    /// <summary>Accumulated survey counts the plugin's ProfileLearner needs to recompute the
    /// profile after another survey (plan D5). Opaque to the API; "{}" until the first survey.</summary>
    public string StatsJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
