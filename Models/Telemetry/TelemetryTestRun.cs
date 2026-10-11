using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// A named test session (e.g. a closed-alpha Siege run, DESIGN.md "First vertical slice"). While it
/// is running its id is stamped on every diagnostic event the plugin emits, and an enhanced target
/// naming it switches enhanced events on for everyone online (IMPLEMENTATION_PLAN.md §1.3).
/// </summary>
public class TelemetryTestRun
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public DateTime StartedAt { get; set; }

    /// <summary>Null while the run is active.</summary>
    public DateTime? EndedAt { get; set; }

    public int CreatedByUserId { get; set; }
}
