using System;

namespace knkwebapi_v2.Models;

public enum EmailOutboxStatus
{
    Pending,
    Sent,
    Failed,
    Expired,
    Suppressed
}

/// <summary>
/// One outgoing mail in the persistent outbox (KNG-206, docs/specs/invitations/DESIGN.md § 4.2).
/// IEmailComposer adds the row in the same transaction as the change that causes it, and
/// EmailDispatcher sends it with retries. Once a row is Sent, Expired or Suppressed, both bodies
/// are cleared so no reset link or code stays in the database; a Failed row keeps them only when
/// it has no <see cref="ExpiresAt"/> (no token), so staff can retry it. Rows are deleted by
/// RetentionPolicyService after 90 days.
/// </summary>
public class EmailOutboxMessage
{
    public long Id { get; set; }

    /// <summary>The EmailKind name, e.g. "PasswordReset".</summary>
    public string Kind { get; set; } = null!;

    /// <summary>Lower-case, trimmed recipient address.</summary>
    public string ToAddress { get; set; } = null!;

    public string? ToName { get; set; }

    public string Subject { get; set; } = null!;

    public string? TextBody { get; set; }

    public string? HtmlBody { get; set; }

    public EmailOutboxStatus Status { get; set; } = EmailOutboxStatus.Pending;

    /// <summary>Send attempts made so far.</summary>
    public int Attempts { get; set; }

    public DateTime NextAttemptAt { get; set; }

    /// <summary>Mails that carry a token expire with it: an unsent row past this becomes Expired.</summary>
    public DateTime? ExpiresAt { get; set; }

    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? SentAt { get; set; }

    /// <summary>The Message-ID the mail was sent with.</summary>
    public string? ProviderMessageId { get; set; }

    public int? UserId { get; set; }

    /// <summary>Unique per logical mail (e.g. "reset:{tokenId}"); queueing the same key twice is a no-op.</summary>
    public string DedupeKey { get; set; } = null!;
}
