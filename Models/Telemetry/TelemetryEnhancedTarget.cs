using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// Switches enhanced diagnostics on for one player or for one test run until <see cref="ExpiresAt"/>
/// (IMPLEMENTATION_PLAN.md §1.3). Exactly one of <see cref="UserId"/> / <see cref="TestRunId"/> is
/// set (check constraint).
/// </summary>
public class TelemetryEnhancedTarget
{
    public int Id { get; set; }

    public int? UserId { get; set; }

    public int? TestRunId { get; set; }

    public DateTime ExpiresAt { get; set; }

    public int CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }
}
