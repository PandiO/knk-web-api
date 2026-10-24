namespace knkwebapi_v2.Configuration;

/// <summary>
/// Settings for the application as a whole (section "App", KNG-206).
/// </summary>
public class AppSettings
{
    public const string SectionName = "App";

    /// <summary>
    /// The web app's public address, e.g. https://knightsandkings.net, used for every link in a
    /// mail. Empty: Security:PasswordResetFrontendBaseUrl and Privacy:FrontendBaseUrl are used as
    /// before, then http://localhost:3000.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>The first non-empty of <paramref name="candidates"/> without a trailing slash, else localhost.</summary>
    public static string ResolveBaseUrl(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim().TrimEnd('/');
            }
        }
        return "http://localhost:3000";
    }
}
