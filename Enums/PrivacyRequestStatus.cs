namespace knkwebapi_v2.Enums;

/// <summary>State of a GDPR deletion request (DESIGN.md §F.14).</summary>
public enum PrivacyRequestStatus : byte
{
    /// <summary>Confirmed (or filed by staff): runs at ScheduledAt, after the grace period, unless cancelled.</summary>
    Pending = 0,
    Completed = 1,
    Cancelled = 2,

    /// <summary>A player's own request waiting for the email confirmation link.</summary>
    AwaitingConfirmation = 3,

    /// <summary>The confirmation link was not used in time; nothing happens.</summary>
    Expired = 4
}
