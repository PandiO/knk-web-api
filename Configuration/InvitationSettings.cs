using System.Collections.Generic;

namespace knkwebapi_v2.Configuration;

/// <summary>
/// Section "Invitations" (KNG-212, docs/specs/invitations/DESIGN.md § 3.2 and decisions I1, I2, I4).
/// </summary>
public class InvitationSettings
{
    public const string SectionName = "Invitations";

    public int DefaultExpiryDays { get; set; } = 7;

    /// <summary>Upper bound for both kinds; there is no "never" during the alpha.</summary>
    public int MaxExpiryDays { get; set; } = 30;

    public int SharedDefaultMaxUses { get; set; } = 5;

    public int SharedMaxUsesCap { get; set; } = 50;

    /// <summary>Group names an invitation may grant on redemption. Staff groups never belong here.</summary>
    public List<string> InvitableGroups { get; set; } = new() { "Alpha Tester" };

    /// <summary>Resends of one personal invite in a rolling 24 hours.</summary>
    public int ResendPerDay { get; set; } = 3;

    /// <summary>Addresses one "create personal invites" request may hold.</summary>
    public int MaxEmailsPerRequest { get; set; } = 100;
}
