namespace knkwebapi_v2.Models;

/// <summary>
/// Marker of a GDPR-erased account (DESIGN.md §F.14): <c>users.DeletedReason</c> of every account a
/// deletion request pseudonymized. Erased accounts never receive statistics again (projections,
/// rebuilds and plugin batches skip them).
/// </summary>
public static class PrivacyErasure
{
    public const string Reason = "GDPR erasure";
}
