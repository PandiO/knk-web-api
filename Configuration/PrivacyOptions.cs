namespace knkwebapi_v2.Configuration;

/// <summary>
/// GDPR deletion (KNG-34 D12, DESIGN.md §F.14, developer decisions 2026-10-03). A player's own
/// request waits <see cref="ConfirmationHours"/> for the emailed confirmation link; a confirmed (or
/// staff-filed) request runs <see cref="GraceDays"/> later unless cancelled. The legal deadline is
/// <see cref="DeletionDueDays"/> after confirmation. With <see cref="AutoExecuteEnabled"/> false the
/// job only expires confirmations and the owner executes scheduled requests by hand.
/// </summary>
public class PrivacyOptions
{
    public const string SectionName = "Privacy";

    public int DeletionDueDays { get; set; } = 30;

    public int GraceDays { get; set; } = 5;

    public int ConfirmationHours { get; set; } = 24;

    /// <summary>Minimum time between two confirmation emails for the same request.</summary>
    public int ResendCooldownSeconds { get; set; } = 60;

    public bool AutoExecuteEnabled { get; set; } = true;

    /// <summary>Web app base URL for the links in emails; empty = Security:PasswordResetFrontendBaseUrl.</summary>
    public string? FrontendBaseUrl { get; set; }
}
