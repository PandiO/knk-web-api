using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Dtos;

// GDPR deletion contracts (KNG-34 link 6, knk-workspace docs/specs/player-statistics/
// IMPLEMENTATION_PLAN.md §3.3, DESIGN.md §F.14; request flow from the developer decisions of
// 2026-10-03: player request + email confirmation, staff filing, grace period).

public class PrivacyDeletionRequestCreateDto
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    /// <summary>Optional (≤ 500 characters), e.g. where the request came from. Not shown to the player.</summary>
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

public class PrivacyDeletionConfirmDto
{
    /// <summary>The token from the emailed confirmation link.</summary>
    [JsonPropertyName("token")]
    public string? Token { get; set; }
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

    /// <summary>Player, Staff or Owner.</summary>
    [JsonPropertyName("source")]
    public PrivacyRequestSource Source { get; set; }

    [JsonPropertyName("requestedAt")]
    public DateTime RequestedAt { get; set; }

    /// <summary>Legal deadline: one month after confirmation.</summary>
    [JsonPropertyName("dueAt")]
    public DateTime DueAt { get; set; }

    [JsonPropertyName("status")]
    public PrivacyRequestStatus Status { get; set; }

    [JsonPropertyName("requestedByUserId")]
    public int RequestedByUserId { get; set; }

    /// <summary>Staff/owner note; never returned to the player.</summary>
    [JsonPropertyName("note")]
    public string? Note { get; set; }

    /// <summary>Until when the emailed link works (AwaitingConfirmation only).</summary>
    [JsonPropertyName("confirmationExpiresAt")]
    public DateTime? ConfirmationExpiresAt { get; set; }

    [JsonPropertyName("confirmedAt")]
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>When the deletion runs (end of the grace period); cancellable until then.</summary>
    [JsonPropertyName("scheduledAt")]
    public DateTime? ScheduledAt { get; set; }

    /// <summary>True when the scheduled job will run it at ScheduledAt (false: the owner executes by hand).</summary>
    [JsonPropertyName("autoExecute")]
    public bool AutoExecute { get; set; }

    [JsonPropertyName("cancelledAt")]
    public DateTime? CancelledAt { get; set; }

    [JsonPropertyName("cancelledByUserId")]
    public int? CancelledByUserId { get; set; }

    [JsonPropertyName("executedAt")]
    public DateTime? ExecutedAt { get; set; }

    /// <summary>Null when executed by the scheduled job.</summary>
    [JsonPropertyName("executedByUserId")]
    public int? ExecutedByUserId { get; set; }

    [JsonPropertyName("result")]
    public PrivacyDeletionResultDto? Result { get; set; }
}

/// <summary>One erased account in the erasure feed (realtime S5, PandiO/knk-workspace#184).</summary>
public class PrivacyErasureFeedItemDto
{
    /// <summary>The feed's sequence; pass the highest one seen as <c>since</c>.</summary>
    public long ErasureId { get; set; }

    public int UserId { get; set; }

    /// <summary>The Minecraft UUID the account had; null when it had none or after Privacy:ErasureFeedUuidRetentionDays.</summary>
    public string? FormerUuid { get; set; }

    public DateTime ErasedAt { get; set; }
}

/// <summary><c>GET api/privacy/erasures?since={id}</c>: the erasures after <c>since</c>, oldest first.</summary>
public class PrivacyErasureFeedDto
{
    public List<PrivacyErasureFeedItemDto> Items { get; set; } = new();

    /// <summary>The highest erasure id in <see cref="Items"/>, or <c>since</c> when there are none.</summary>
    public long LastId { get; set; }

    /// <summary>More rows follow; ask again with <see cref="LastId"/>.</summary>
    public bool HasMore { get; set; }

    /// <summary>The newest erasure id the feed has (0 when empty). A <c>since</c> above it means the caller's position
    /// came from another database (a reset): start again from 0.</summary>
    public long LatestId { get; set; }
}
