using System;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// One diagnostic event (KNG-34, knk-workspace docs/specs/player-statistics/IMPLEMENTATION_PLAN.md
/// §1.3, DESIGN.md §F.12). Owner-only (§F.13). Holds ids and allowlisted scalar payload keys only —
/// never chat text, command arguments, IPs, tokens or raw bodies. Ledger amounts and match results
/// are not copied: reads link to the authoritative rows by correlation id, user and time.
/// No FK to users (GDPR deletion removes the rows explicitly).
/// </summary>
public class TelemetryEvent
{
    public long Id { get; set; }

    /// <summary>Source-generated UUID; the dedupe key.</summary>
    public Guid EventId { get; set; }

    /// <summary><c>&lt;family&gt;.&lt;event&gt;</c>, e.g. <c>siege.match_join</c>.</summary>
    public string Name { get; set; } = null!;

    public short SchemaVersion { get; set; } = 1;

    public TelemetryLevel Level { get; set; }

    public TelemetrySource Source { get; set; }

    public DateTime OccurredAt { get; set; }

    public DateTime ReceivedAt { get; set; }

    public string ServerName { get; set; } = null!;

    /// <summary>Monotonic per source instance; orders events with the same timestamp.</summary>
    public long ServerSeq { get; set; }

    public string AppVersion { get; set; } = null!;

    public int? UserId { get; set; }

    public Guid? SessionKey { get; set; }

    public int? TestRunId { get; set; }

    public int? MatchId { get; set; }

    public string? CorrelationId { get; set; }

    public string Feature { get; set; } = null!;

    public string Action { get; set; } = null!;

    public TelemetryOutcome Outcome { get; set; }

    /// <summary>Stable code, never free text.</summary>
    public string? ReasonCode { get; set; }

    public string? ObjectType { get; set; }

    public string? ObjectId { get; set; }

    /// <summary>Allowlisted scalar keys (≤ 16) as a JSON object, or null.</summary>
    public string? PayloadJson { get; set; }
}
