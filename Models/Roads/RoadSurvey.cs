using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// One finished survey walk (docs/specs/navigation/DESIGN.md §3.2, §5.3): who walked where, the
/// RDP-simplified breadcrumb and the material/width statistics. Kept for coverage checks and to
/// re-derive a profile when the learning rules change. Written by the plugin only.
/// </summary>
public class RoadSurvey
{
    public int Id { get; set; }

    public string World { get; set; } = null!;

    /// <summary>The profile the samples were merged into; SetNull when the profile is deleted.</summary>
    public int? ProfileId { get; set; }
    public RoadProfile? Profile { get; set; }

    /// <summary>The in-game staff member (X-Acting-User-Id); Restrict, users are soft-deleted.</summary>
    public int? StartedByUserId { get; set; }
    public User? StartedBy { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    public int SampleCount { get; set; }

    /// <summary>JSON [{x, y, z, onRoad}] polyline of the walked path, RDP-simplified.</summary>
    public string BreadcrumbJson { get; set; } = "[]";

    /// <summary>JSON: material histogram by lateral offset and width histogram (opaque to the API).</summary>
    public string StatsJson { get; set; } = "{}";
}
