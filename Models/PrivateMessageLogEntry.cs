using System;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// One private message (/msg, /reply) as knk-plugin shipped it for moderation
/// (KNG-18 Phase 3, docs/specs/private-messages/DESIGN.md §3.1). Append-only, written only by
/// the game server (POST api/private-message-log/batch) and read by staff holding
/// knk.pmlog.read, every read audited. Rows older than
/// <see cref="AuditLogRetentionConfiguration.PrivateMessageRetentionDays"/> (default 30) are deleted
/// by RetentionPolicyService.
///
/// SenderUserId/RecipientUserId are plain columns with no FK, like AuditLogEntry: the log must keep
/// saying "user 42" after that user is deleted. Null means the console (or a player the API has
/// no user for); the names at send time are kept either way.
/// </summary>
public class PrivateMessageLogEntry
{
    public const int NameMaxLength = 16;
    public const int ContentMaxLength = 512;

    public long Id { get; set; }

    /// <summary>When the plugin handled the message (its clock, UTC).</summary>
    public DateTime SentAt { get; set; }

    /// <summary>Plugin-generated; unique, so re-sending a batch after a timeout stores nothing twice.</summary>
    public Guid ClientMessageId { get; set; }

    public int? SenderUserId { get; set; }

    public string SenderName { get; set; } = null!;

    public int? RecipientUserId { get; set; }

    public string RecipientName { get; set; } = null!;

    /// <summary>Plain text as typed (never formatted).</summary>
    public string Content { get; set; } = null!;

    public PrivateMessageOutcome Outcome { get; set; }

    /// <summary>Sent with /reply rather than /msg.</summary>
    public bool ViaReply { get; set; }
}
