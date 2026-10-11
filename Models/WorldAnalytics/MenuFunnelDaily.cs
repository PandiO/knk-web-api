using System;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// How often a menu step happened on one local day (KNG-34 link 7, IMPLEMENTATION_PLAN.md §1.4,
/// DESIGN.md D11). Steps: <c>opened</c>, <c>action:&lt;actionTypeId&gt;</c>, <c>back</c>,
/// <c>closed</c>. Anonymous counts only.
/// </summary>
public class MenuFunnelDaily
{
    public DateOnly Day { get; set; }

    /// <summary>Menu template key (e.g. <c>profile.main</c>).</summary>
    public string MenuKey { get; set; } = null!;

    public string Step { get; set; } = null!;

    /// <summary>Succeeded/Denied/Failed for actions; Info for opened/back/closed.</summary>
    public TelemetryOutcome Outcome { get; set; }

    public int Count { get; set; }
}
