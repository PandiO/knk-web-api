namespace knkwebapi_v2.Enums;

/// <summary>State of a GDPR deletion request (DESIGN.md §F.14).</summary>
public enum PrivacyRequestStatus : byte
{
    Pending = 0,
    Completed = 1,
    Cancelled = 2
}
