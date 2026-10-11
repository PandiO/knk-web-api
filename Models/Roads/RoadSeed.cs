using System;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// A point the road mask grows from (docs/specs/navigation/DESIGN.md §3.4, §5.4): placed by an
/// admin or taken from a survey breadcrumb. Domain-Location seeds are derived at build time and
/// never stored.
/// </summary>
public class RoadSeed
{
    public int Id { get; set; }

    public string World { get; set; } = null!;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    public RoadSeedSource Source { get; set; } = RoadSeedSource.Admin;

    /// <summary>The survey the seed came from; SetNull when that survey is deleted.</summary>
    public int? SurveyId { get; set; }
    public RoadSurvey? Survey { get; set; }

    /// <summary>Max 200.</summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
