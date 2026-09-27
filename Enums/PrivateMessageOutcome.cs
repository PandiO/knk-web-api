namespace knkwebapi_v2.Enums;

/// <summary>
/// What happened to a private message logged in <see cref="knkwebapi_v2.Models.PrivateMessageLogEntry"/>
/// (docs/specs/private-messages/DESIGN.md §3.1). Mirrors knk-plugin's
/// <c>PrivateMessageLogger.Outcome</c>; serialized and stored as its PascalCase name.
/// </summary>
public enum PrivateMessageOutcome
{
    Delivered = 0,

    /// <summary>The recipient ignores the sender; the sender saw the normal echo.</summary>
    BlockedIgnored = 1,

    BlockedRateLimited = 2,

    /// <summary>A frozen sender messaged someone without knk.freeze.</summary>
    BlockedFrozen = 3
}
