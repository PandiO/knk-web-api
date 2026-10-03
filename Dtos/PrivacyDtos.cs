using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Dtos;

// GDPR deletion contracts (KNG-34 link 6, knk-workspace docs/specs/player-statistics/
// IMPLEMENTATION_PLAN.md §3.3, DESIGN.md §F.14). Owner only.

public class PrivacyDeletionRequestCreateDto
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    /// <summary>Optional (≤ 500 characters), e.g. where the request came from. Not shown to anyone else.</summary>
    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

/// <summary>Counts of what an erasure removes (or removed); never the data itself.</summary>
public class PrivacyDeletionResultDto
{
    /// <summary>True for a preview: nothing was changed.</summary>
    [JsonPropertyName("dryRun")]
    public bool DryRun { get; set; }

    /// <summary>The player plus every account merged into them.</summary>
    [JsonPropertyName("userIds")]
    public List<int> UserIds { get; set; } = new();

    /// <summary>Rows per table.</summary>
    [JsonPropertyName("deleted")]
    public SortedDictionary<string, int> Deleted { get; set; } = new();

    /// <summary>users rows pseudonymized (username → deleted-&lt;id&gt;, identifiers cleared, inactive).</summary>
    [JsonPropertyName("pseudonymizedUsers")]
    public int PseudonymizedUsers { get; set; }
}

public class PrivacyDeletionRequestDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    /// <summary>Current username (the pseudonym once executed).</summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("requestedAt")]
    public DateTime RequestedAt { get; set; }

    [JsonPropertyName("dueAt")]
    public DateTime DueAt { get; set; }

    /// <summary>When the due-date job will execute a pending request (null when auto-execution is off).</summary>
    [JsonPropertyName("autoExecuteAt")]
    public DateTime? AutoExecuteAt { get; set; }

    [JsonPropertyName("status")]
    public PrivacyRequestStatus Status { get; set; }

    [JsonPropertyName("requestedByUserId")]
    public int RequestedByUserId { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("executedAt")]
    public DateTime? ExecutedAt { get; set; }

    /// <summary>Null when executed by the due-date job.</summary>
    [JsonPropertyName("executedByUserId")]
    public int? ExecutedByUserId { get; set; }

    [JsonPropertyName("result")]
    public PrivacyDeletionResultDto? Result { get; set; }
}
