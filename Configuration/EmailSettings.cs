namespace knkwebapi_v2.Configuration;

/// <summary>
/// Outbound email delivery configuration (section "Email"). SmtpPassword is intentionally left out
/// of appsettings*.json and must be supplied via environment variable (Email__SmtpPassword)
/// or user-secrets - never commit it to source control. See docs/guides/email.md in knk-workspace.
/// </summary>
public class EmailSettings
{
    public const string SectionName = "Email";

    public const string SmtpTransport = "Smtp";
    public const string LogTransport = "Log";
    public const string PickupTransport = "Pickup";

    /// <summary>
    /// "Smtp" (sends real mail), "Log" (logs instead of sending; links only in Development) or
    /// "Pickup" (writes .eml files to <see cref="PickupDirectory"/>). Empty: <see cref="Provider"/>.
    /// </summary>
    public string Transport { get; set; } = string.Empty;

    /// <summary>The setting before KNG-206 ("Log" or "Smtp"); read when <see cref="Transport"/> is empty.</summary>
    public string Provider { get; set; } = LogTransport;

    /// <summary>
    /// The Log transport writes reset links to the log, so outside Development the API refuses to
    /// start with it unless this is true (an emergency switch, e.g. while the mail relay is down).
    /// </summary>
    public bool AllowLogTransport { get; set; }

    public string SmtpHost { get; set; } = "smtp.gmail.com";

    public int SmtpPort { get; set; } = 587;

    /// <summary>True: STARTTLS when the server offers it. False: plain (an internal relay hop).
    /// Port 465 always uses TLS on connect.</summary>
    public bool UseStartTls { get; set; } = true;

    /// <summary>
    /// Mailbox/account used to authenticate with the SMTP server. Empty: send without
    /// authentication (an internal relay such as the alpha's mailrelay container, KNG-211).
    /// </summary>
    public string SmtpUsername { get; set; } = string.Empty;

    /// <summary>
    /// SMTP password / app password. Populate via environment variable or user-secrets only.
    /// Required when <see cref="SmtpUsername"/> is set.
    /// </summary>
    public string SmtpPassword { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "Knights & Kings";

    /// <summary>The Reply-To address (decision E3: support@knightsandkings.net once it is monitored).
    /// Empty: no Reply-To header.</summary>
    public string ReplyToAddress { get; set; } = string.Empty;

    /// <summary>Folder for the Pickup transport's .eml files.</summary>
    public string PickupDirectory { get; set; } = "mail-pickup";

    /// <summary>Mails queued across all recipients in a rolling 24 hours before new ones are Suppressed.</summary>
    public int DailyCap { get; set; } = 300;

    /// <summary>Send attempts before a mail is marked Failed.</summary>
    public int MaxAttempts { get; set; } = 6;

    /// <summary>The transport in effect: <see cref="Transport"/>, else <see cref="Provider"/>, else Log.</summary>
    public string EffectiveTransport()
    {
        var value = !string.IsNullOrWhiteSpace(Transport) ? Transport.Trim()
            : !string.IsNullOrWhiteSpace(Provider) ? Provider.Trim()
            : LogTransport;
        if (string.Equals(value, SmtpTransport, StringComparison.OrdinalIgnoreCase)) return SmtpTransport;
        if (string.Equals(value, PickupTransport, StringComparison.OrdinalIgnoreCase)) return PickupTransport;
        return LogTransport;
    }
}
