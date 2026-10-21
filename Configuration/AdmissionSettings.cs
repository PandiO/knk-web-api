namespace knkwebapi_v2.Configuration;

/// <summary>
/// Section "Admission" (KNG-214, knk-workspace docs/specs/invitations/DESIGN.md § 5.2): the game
/// server's pre-login gate. The gate only stops anyone while Security:Registration:Mode is Invite.
/// </summary>
public class AdmissionSettings
{
    public const string SectionName = "Admission";

    /// <summary>How long a claim code from the disconnect screen stays valid.</summary>
    public int ClaimCodeMinutes { get; set; } = 15;

    /// <summary>An invited account still without Minecraft after this many days gets one
    /// FinishLinking reminder mail (0 turns the reminder off).</summary>
    public int FinishLinkingReminderDays { get; set; } = 2;
}
