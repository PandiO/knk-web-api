using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos;

// ===== Currency monitor: alerts and reconciliation (currency-payments IMPLEMENTATION_PLAN.md Phase 5) =====

/// <summary>One currency anomaly alert (DESIGN.md §3.9) for the staff alerts page and /knk currency alerts.</summary>
public class CurrencyAlertDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>R1–R9.</summary>
    [JsonPropertyName("rule")]
    public string Rule { get; set; } = null!;

    /// <summary>e.g. "Funnel", "Staff adjustments".</summary>
    [JsonPropertyName("ruleName")]
    public string RuleName { get; set; } = null!;

    /// <summary>Low, Medium, High or Critical.</summary>
    [JsonPropertyName("severity")]
    public string Severity { get; set; } = null!;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    /// <summary>The player concerned (the recipient, the staff member, the capped player…), if any.</summary>
    [JsonPropertyName("userId")]
    public int? UserId { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("transactionId")]
    public long? TransactionId { get; set; }

    /// <summary>The transaction's public id (TX …), for a link to its detail page.</summary>
    [JsonPropertyName("transactionPublicId")]
    public string? TransactionPublicId { get; set; }

    /// <summary>Rule-specific facts (thresholds, counts, the mismatches…).</summary>
    [JsonPropertyName("details")]
    public JsonElement? Details { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("ackedAt")]
    public DateTime? AckedAt { get; set; }

    [JsonPropertyName("ackedByUserId")]
    public int? AckedByUserId { get; set; }

    [JsonPropertyName("ackedByUsername")]
    public string? AckedByUsername { get; set; }
}

/// <summary>A page of alerts plus how many are still open (the page's badge).</summary>
public class CurrencyAlertPageDto : PagedResultDto<CurrencyAlertDto>
{
    [JsonPropertyName("openCount")]
    public int OpenCount { get; set; }

    /// <summary>Open alerts per severity name.</summary>
    [JsonPropertyName("openBySeverity")]
    public Dictionary<string, int> OpenBySeverity { get; set; } = new();
}

/// <summary>Filters of GET api/currency/admin/alerts.</summary>
public class CurrencyAlertQuery
{
    /// <summary>open (default), acked or all.</summary>
    public string Status { get; set; } = "open";

    /// <summary>This severity and above.</summary>
    public Models.CurrencyAlertSeverity? MinSeverity { get; set; }

    /// <summary>One rule id, e.g. R3.</summary>
    public string? Rule { get; set; }

    public int? UserId { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 25;
}

/// <summary>One reconciliation run (rules R1/R2): the users balance columns against the ledger.</summary>
public class CurrencyReconciliationRunDto
{
    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; }

    [JsonPropertyName("finishedAt")]
    public DateTime? FinishedAt { get; set; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    /// <summary>"scheduled" or "manual".</summary>
    [JsonPropertyName("trigger")]
    public string Trigger { get; set; } = null!;

    /// <summary>Staff member who started a manual run.</summary>
    [JsonPropertyName("triggeredByUserId")]
    public int? TriggeredByUserId { get; set; }

    [JsonPropertyName("mismatchCount")]
    public int MismatchCount { get; set; }

    /// <summary>The mismatches, at most <see cref="MaxListed"/>.</summary>
    [JsonPropertyName("mismatches")]
    public List<CurrencyMismatchDto> Mismatches { get; set; } = new();

    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }

    /// <summary>Set when the run failed (nothing is known about the balances then).</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>Alerts this run raised (none when the same mismatches were already reported today).</summary>
    [JsonPropertyName("alertIds")]
    public List<long> AlertIds { get; set; } = new();

    /// <summary>Currencies whose player transfers this run switched off (R1 kill switch).</summary>
    [JsonPropertyName("transfersDisabled")]
    public List<string> TransfersDisabled { get; set; } = new();

    public const int MaxListed = 200;
}

/// <summary>GET api/currency/admin/reconciliation: the last run since the API started.</summary>
public class CurrencyReconciliationStatusDto
{
    /// <summary>Null until the first run after startup.</summary>
    [JsonPropertyName("lastRun")]
    public CurrencyReconciliationRunDto? LastRun { get; set; }

    [JsonPropertyName("running")]
    public bool Running { get; set; }

    [JsonPropertyName("monitorEnabled")]
    public bool MonitorEnabled { get; set; }

    [JsonPropertyName("intervalMinutes")]
    public int IntervalMinutes { get; set; }
}

/// <summary>Payload of a CurrencyAlert player notification: shown in-game to online staff holding knk.admin.currency.alerts.</summary>
public class CurrencyAlertNotificationDto
{
    [JsonPropertyName("alertId")]
    public long AlertId { get; set; }

    [JsonPropertyName("rule")]
    public string Rule { get; set; } = null!;

    [JsonPropertyName("ruleName")]
    public string RuleName { get; set; } = null!;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = null!;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("userId")]
    public int? UserId { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>Currencies whose player transfers were switched off by this alert (R1).</summary>
    [JsonPropertyName("transfersDisabled")]
    public List<string> TransfersDisabled { get; set; } = new();
}
