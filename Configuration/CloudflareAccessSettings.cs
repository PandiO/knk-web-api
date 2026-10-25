using System;

namespace knkwebapi_v2.Configuration;

/// <summary>
/// Section "CloudflareAccess" (KNG-216, knk-workspace docs/specs/invitations/DESIGN.md § 5.8,
/// decision I8): the API keeps a Zero Trust email list in sync with who may open the site. Off by
/// default; with SyncEnabled=false nothing calls Cloudflare and staff edit the list by hand.
/// <para>
/// ApiToken is a secret: set it only through the environment (CloudflareAccess__ApiToken), never
/// in appsettings*.json.
/// </para>
/// </summary>
public class CloudflareAccessSettings
{
    public const string SectionName = "CloudflareAccess";

    /// <summary>The named HttpClient every Cloudflare call goes through.</summary>
    public const string HttpClientName = "cloudflare";

    public const string DefaultBaseUrl = "https://api.cloudflare.com/client/v4/";

    public bool SyncEnabled { get; set; }

    public string? AccountId { get; set; }

    /// <summary>The Zero Trust list (type "User email addresses") the API owns. Everything in it
    /// that isn't in the desired set is removed, so nobody edits it by hand.</summary>
    public string? ListId { get; set; }

    public string? ApiToken { get; set; }

    /// <summary>Forward proxy for the cloudflare client only, e.g. http://cfproxy:8888 (the API
    /// container has no internet of its own).</summary>
    public string? ProxyUrl { get; set; }

    public int ReconcileMinutes { get; set; } = 5;

    public int InlineAddTimeoutSeconds { get; set; } = 5;

    /// <summary>Safety valve: a run that would remove more entries than this removes none and
    /// reports it as the sync error, so a wrong desired set can't empty the list.</summary>
    public int MaxRemovalsPerRun { get; set; } = 25;

    public int RequestTimeoutSeconds { get; set; } = 10;

    public string BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>Why an enabled sync can't run, or null when it can (or is off).</summary>
    public string? ConfigurationProblem()
    {
        if (!SyncEnabled) return null;
        if (string.IsNullOrWhiteSpace(AccountId)) return "CloudflareAccess:AccountId is not set.";
        if (string.IsNullOrWhiteSpace(ListId)) return "CloudflareAccess:ListId is not set.";
        if (string.IsNullOrWhiteSpace(ApiToken)) return "CloudflareAccess:ApiToken is not set (environment only).";
        if (!string.IsNullOrWhiteSpace(ProxyUrl) && !Uri.TryCreate(ProxyUrl, UriKind.Absolute, out _))
            return "CloudflareAccess:ProxyUrl is not an absolute URL.";
        return null;
    }

    public TimeSpan ReconcileInterval => TimeSpan.FromMinutes(Math.Max(1, ReconcileMinutes));

    public TimeSpan InlineAddTimeout => TimeSpan.FromSeconds(Math.Clamp(InlineAddTimeoutSeconds, 1, 30));
}
