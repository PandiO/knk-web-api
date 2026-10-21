using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos;

/// <summary>One mail on /admin/email (KNG-206): metadata only, the recipient masked, never a body.</summary>
public class EmailOutboxItemDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = null!;

    [JsonPropertyName("toMasked")]
    public string ToMasked { get; set; } = null!;

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = null!;

    [JsonPropertyName("status")]
    public string Status { get; set; } = null!;

    [JsonPropertyName("attempts")]
    public int Attempts { get; set; }

    [JsonPropertyName("nextAttemptAt")]
    public DateTime NextAttemptAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    [JsonPropertyName("lastError")]
    public string? LastError { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("sentAt")]
    public DateTime? SentAt { get; set; }

    [JsonPropertyName("userId")]
    public int? UserId { get; set; }

    /// <summary>True for a Failed mail that still has its bodies (it carried no token).</summary>
    [JsonPropertyName("canRetry")]
    public bool CanRetry { get; set; }
}

public class EmailOutboxPageDto
{
    [JsonPropertyName("items")]
    public List<EmailOutboxItemDto> Items { get; set; } = new();

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("pageNumber")]
    public int PageNumber { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    /// <summary>The transport in effect: Smtp, Log or Pickup.</summary>
    [JsonPropertyName("transport")]
    public string Transport { get; set; } = null!;

    /// <summary>Mails queued (not suppressed) in the last 24 hours, against <see cref="DailyCap"/>.</summary>
    [JsonPropertyName("last24Hours")]
    public int Last24Hours { get; set; }

    [JsonPropertyName("dailyCap")]
    public int DailyCap { get; set; }
}
