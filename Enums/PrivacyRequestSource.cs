namespace knkwebapi_v2.Enums;

/// <summary>Who filed a GDPR deletion request (developer decision 2026-10-03, DESIGN.md §F.14).</summary>
public enum PrivacyRequestSource : byte
{
    /// <summary>The player, on the web app; needs the email confirmation.</summary>
    Player = 0,

    /// <summary>Staff with knk.admin.privacy.request, for a player; no email confirmation.</summary>
    Staff = 1,

    /// <summary>The owner, on the owner privacy page; no email confirmation.</summary>
    Owner = 2
}
