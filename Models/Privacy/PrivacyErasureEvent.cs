using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// One account a GDPR erasure pseudonymized (realtime S5, PandiO/knk-workspace#184, DESIGN §7.4): the persisted feed the
/// game servers catch up from (<c>GET api/privacy/erasures?since={id}</c>) after the at-most-once <c>AccountErased</c>
/// push. <see cref="Id"/> is the sequence. Written in the erasure's own transaction, one row per erased account (the
/// player and every account merged into them).
/// <para>The former Minecraft UUID is the one piece of personal data left about the account: game servers need it to
/// delete what they keep under that UUID. It is cleared <c>Privacy:ErasureFeedUuidRetentionDays</c> (35) after the
/// erasure, past the game server's own 30-day maximum age for anything it keeps (KNG-58); the row stays so the sequence
/// has no holes that look like missed events.</para>
/// </summary>
public class PrivacyErasureEvent
{
    public long Id { get; set; }

    /// <summary>The erased (now pseudonymized) account.</summary>
    public int UserId { get; set; }

    /// <summary>Its Minecraft UUID before the erasure; null when it had none, or once the retention passed.</summary>
    public string? FormerUuid { get; set; }

    /// <summary>The executed deletion request.</summary>
    public int DeletionRequestId { get; set; }

    public DateTime ErasedAt { get; set; }

    /// <summary>When <see cref="FormerUuid"/> was cleared after the retention period.</summary>
    public DateTime? UuidClearedAt { get; set; }
}
